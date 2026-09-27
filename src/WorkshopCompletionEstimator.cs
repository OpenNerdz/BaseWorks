using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    internal sealed class WorkshopCompletionEstimate
    {
        internal double? Seconds;
        internal string Waiting = "";
        internal string Label
        {
            get
            {
                if (!Seconds.HasValue) return "ETA: " + (Waiting.Length > 15 ? "waiting" : Waiting);
                long seconds = (long)Math.Ceiling(Math.Max(0, Seconds.Value));
                if (seconds == 0) return "ETA: complete";
                if (seconds < 60) return "ETA: ~" + seconds + "s";
                if (seconds < 3600) return "ETA: ~" + (seconds / 60) + "m " + (seconds % 60) + "s";
                return "ETA: ~" + (seconds / 3600) + "h " + ((seconds % 3600) / 60) + "m";
            }
        }
        internal string Detail = "";
        internal static WorkshopCompletionEstimate Blocked(string reason, string detail = "")
        { return new WorkshopCompletionEstimate { Waiting = reason, Detail = detail }; }
    }

    // A read-only projection of the selected bill of materials. Native queues
    // use their remaining clocks; planned batches use station-specific staged
    // recipes, shared machine calendars and the same forge feed planner as the
    // real scheduler. No machine/world arrays are ever replaced or committed.
    // Future batches are modeled as complete before dependent batches start:
    // rolling collection can therefore finish earlier than this conservative ETA.
    internal sealed class WorkshopCompletionEstimator
    {
        internal const double CollectionSeconds = 2;
        private const int MaximumWaves = 1024;

        private sealed class Lot
        {
            internal long Count;
            internal double Ready;
            internal Lot Clone() { return new Lot { Count = Count, Ready = Ready }; }
        }

        private sealed class Inventory
        {
            internal readonly Dictionary<int, List<Lot>> Items = new Dictionary<int, List<Lot>>();
            internal void Add(int type, long count, double ready)
            {
                if (count <= 0) return;
                List<Lot> lots;
                if (!Items.TryGetValue(type, out lots)) Items[type] = lots = new List<Lot>();
                lots.Add(new Lot { Count = count, Ready = ready });
            }
            internal Inventory Clone()
            {
                var copy = new Inventory();
                foreach (var pair in Items) copy.Items[pair.Key] = pair.Value.Select(l => l.Clone()).ToList();
                return copy;
            }
            internal double Take(int type, long count, bool consume = true)
            {
                if (count <= 0) return 0;
                List<Lot> lots;
                if (!Items.TryGetValue(type, out lots)) return double.PositiveInfinity;
                double ready = 0;
                foreach (var lot in lots.OrderBy(l => l.Ready))
                {
                    long taken = Math.Min(count, lot.Count);
                    if (taken == 0) continue;
                    ready = Math.Max(ready, lot.Ready); count -= taken;
                    if (consume) lot.Count -= taken;
                    if (count == 0) return ready;
                }
                return double.PositiveInfinity;
            }
        }

        private sealed class Machine
        {
            internal TileEntityWorkstation Station;
            internal ItemStack[] Input;
            internal float[] Timers;
            internal double Available, ForgeClock, Fuel, WorkFuel;
            internal string Blocked;
            internal Machine Clone()
            {
                return new Machine { Station = Station, Input = ItemStack.Clone(Input), Timers = (float[])Timers.Clone(),
                    Available = Available, ForgeClock = ForgeClock, Fuel = Fuel, WorkFuel = WorkFuel, Blocked = Blocked };
            }
        }

        private sealed class State
        {
            internal Inventory Stock = new Inventory();
            internal Dictionary<TileEntityWorkstation, Machine> Machines = new Dictionary<TileEntityWorkstation, Machine>();
            internal State Clone()
            {
                return new State { Stock = Stock.Clone(), Machines = Machines.ToDictionary(p => p.Key, p => p.Value.Clone()) };
            }
        }

        private sealed class Candidate
        {
            internal WorkshopProductionMachine Option;
            internal int Capacity, Assigned;
            internal double Start, Rate;
        }

        private readonly StorageNetworkSession network;
        private readonly WorkshopControllerData controller;
        private readonly WorkshopProductionPlanner planner;
        private readonly List<TileEntityWorkstation> stations;
        private readonly Inventory outstanding = new Inventory();
        private State state;
        private int waves;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        internal WorkshopCompletionEstimator(StorageNetworkSession network, EntityPlayerLocal player, XUi xui,
            WorkshopControllerData controller, List<TileEntityWorkstation> stations)
        {
            this.network = network; this.controller = controller;
            this.stations = stations.Where(s => s != null && !controller.ExcludedStations.Contains(s.ToWorldPos().ToString())).ToList();
            var types = controller.Targets.Where(t => t.Enabled && t.CompletedUtcTicks == 0).Select(t => t.Item)
                .Concat(controller.Smelting.Select(j => j.Item))
                .Select(name => ItemClass.GetItem(name, false)).Where(i => i != null && !i.IsEmpty()).Select(i => i.type);
            planner = new WorkshopProductionPlanner(network, player, xui, controller, this.stations, controller.AutoCraft, types);
            state = Snapshot();
        }

        internal Dictionary<string, WorkshopCompletionEstimate> Estimate()
        {
            var result = new Dictionary<string, WorkshopCompletionEstimate>();
            var pool = planner.CreatePool();
            foreach (var target in controller.Targets)
            {
                waves = 0;
                if (!controller.Enabled || !target.Enabled)
                { result[target.Item] = WorkshopCompletionEstimate.Blocked("paused"); continue; }
                if (!network.IsAvailable || network.AutomationBusy)
                { result[target.Item] = WorkshopCompletionEstimate.Blocked("storage in use"); continue; }
                var item = ItemClass.GetItem(target.Item, false);
                if (item == null || item.IsEmpty())
                { result[target.Item] = WorkshopCompletionEstimate.Blocked("item unavailable"); continue; }
                long pending = stations.Sum(s => WorkshopManager.CountQueued(s, item.type) + WorkshopManager.CountOutput(s, item.type));
                long leases = controller.Smelting.Where(j => j.Owner == target.Item && j.Item == target.Item).Sum(j => (long)j.Count);
                long deficit = target.Once ? target.Remaining : Math.Max(0, target.Target - network.CountProduct(item.type) - pending);
                long requested = Math.Max(0, deficit - leases);
                long awaiting = target.Once ? Math.Max(0, target.Queued - target.Returned)
                    : Math.Min(pending, Math.Max(0, target.Target - network.CountProduct(item.type)));
                if (target.Once && target.TrackDelivery && awaiting > pending)
                { result[target.Item] = WorkshopCompletionEstimate.Blocked("check output", "Some queued output is no longer in the enabled machines."); continue; }
                double ready = outstanding.Take(item.type, awaiting + Math.Min(deficit, leases), false);
                if (pending > 0 && !OutstandingFits())
                { result[target.Item] = WorkshopCompletionEstimate.Blocked("needs space", "Free connected storage space for the queued output."); continue; }
                var plan = planner.Plan(item.type, (int)Math.Min(WorkshopRules.MaximumTarget, requested), pool, null);
                var projection = state.Clone();
                string blocked;
                double planned;
                bool scheduled = Schedule(plan, projection, out planned, out blocked);
                if (scheduled) state = projection;
                if (plan.Maximum < requested)
                    result[target.Item] = WorkshopCompletionEstimate.Blocked("needs supplies", plan.Missing);
                else if (!scheduled || !Finite(ready))
                    result[target.Item] = WorkshopCompletionEstimate.Blocked(string.IsNullOrEmpty(blocked) ? "waiting for machines" : blocked);
                else result[target.Item] = new WorkshopCompletionEstimate { Seconds = Math.Max(ready, planned),
                    Detail = "Includes current queues, ingredient production, smelting, machine speeds and collection. "
                        + "Assumes the area stays loaded and automation can run. Full-batch planning is conservative; rolling production may finish earlier." };
            }
            return result;
        }

        private State Snapshot()
        {
            var snapshot = new State();
            foreach (var pair in network.SnapshotProductCounts(true)) snapshot.Stock.Add(pair.Key, pair.Value, 0);
            foreach (var station in stations)
            {
                var machine = new Machine { Station = station, Input = ItemStack.Clone(station.Input),
                    Timers = Enumerable.Range(0, Math.Min(station.InputSlotCount, station.Input.Length)).Select(station.GetTimerForSlot).ToArray(),
                    Fuel = station.BurnTotalTimeLeft };
                if (station.IsUserAccessing() || station.IsRemoving || !WorkshopManager.Accessible(station)) machine.Blocked = "machine in use";
                if (station.IsBesideWater) machine.Blocked = "machine blocked by water";
                string status;
                if (WorkshopManager.StationStatus.TryGetValue(station.ToWorldPos(), out status)
                    && status != null && status.StartsWith("Waiting for", StringComparison.Ordinal)
                    && status.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0) machine.Blocked = "needs output space";
                snapshot.Machines[station] = machine;
                foreach (var output in station.Output.Where(s => s != null && !s.IsEmpty()))
                    AddOutstanding(snapshot, output.itemValue.type, output.count, string.IsNullOrEmpty(machine.Blocked) ? CollectionSeconds : double.PositiveInfinity);
                double seconds = 0;
                // Native workstations process the last occupied queue slot first.
                foreach (var queue in station.Queue.Reverse())
                {
                    if (queue == null) continue;
                    if (queue.RepairItem != null && !queue.RepairItem.IsEmpty()) { machine.Blocked = "waiting for repair queue"; continue; }
                    if (queue.Recipe == null || queue.Multiplier <= 0) continue;
                    double duration = QueueSeconds(queue);
                    if (!Finite(duration)) machine.Blocked = "waiting for queue timer";
                    else if (!Fuel(snapshot.Stock, machine, duration, false)) machine.Blocked = "needs fuel";
                    else Fuel(snapshot.Stock, machine, duration, true);
                    seconds += duration;
                    AddOutstanding(snapshot, queue.Recipe.itemValueType, (long)queue.Multiplier * queue.Recipe.count,
                        string.IsNullOrEmpty(machine.Blocked) ? seconds + CollectionSeconds : double.PositiveInfinity);
                }
                machine.Available = seconds;
            }
            foreach (var job in controller.Smelting)
            {
                var item = ItemClass.GetItem(job.Item, false);
                var station = stations.Find(s => s.ToWorldPos() == job.Position);
                if (item == null || item.IsEmpty()) continue;
                double completion = double.PositiveInfinity;
                if (station != null)
                {
                    // Use the same staged recipe key as the scheduler's persisted lease.
                    var option = planner.MachinesFor(job.RecipeKey).FirstOrDefault(m => m.Station == station);
                    var recipe = option == null ? null : option.Recipe;
                    if (recipe != null)
                    {
                        Machine after; WorkshopForgePlan feed; double finish; string reason;
                        if (Probe(snapshot, station, recipe, job.Batches, 0, out after, out feed, out finish, out reason)
                            && Commit(snapshot, station, recipe, job.Batches, after, feed, finish)) completion = finish + CollectionSeconds;
                    }
                }
                AddOutstanding(snapshot, item.type, job.Count, completion);
            }
            return snapshot;
        }

        private void AddOutstanding(State snapshot, int type, long count, double ready)
        { snapshot.Stock.Add(type, count, ready); outstanding.Add(type, count, ready); }

        private bool OutstandingFits()
        {
            var outputs = WorkshopManager.OutstandingOutputs(stations);
            return outputs.Count == 0 || network.CanReserveProducts(outputs.Skip(1).ToList(), outputs[0]);
        }

        internal static double QueueSeconds(RecipeQueueItem queue)
        {
            double each = queue.OneItemCraftTime;
            double first = queue.CraftingTimeLeft;
            if (!Finite(each) || each <= 0 || !Finite(first) || first < 0) return double.PositiveInfinity;
            return first + Math.Max(0, queue.Multiplier - 1) * each;
        }

        private bool Schedule(WorkshopProductionPlan plan, State projection, out double finish, out string blocked)
        {
            finish = 0; blocked = "";
            foreach (var step in plan.Steps)
            {
                double inputsReady = 0;
                if (!step.Recipe.materialBasedRecipe)
                    foreach (var input in step.Recipe.ingredients)
                        inputsReady = Math.Max(inputsReady, projection.Stock.Take(input.itemValue.type, (long)input.count * step.Batches));
                if (!Finite(inputsReady)) { blocked = "waiting for ingredients"; return false; }
                int remaining = step.Batches;
                var machines = planner.MachinesFor(step.Recipe).ToList();
                while (remaining > 0)
                {
                    if (++waves > MaximumWaves) { blocked = "plan too large to estimate"; return false; }
                    var candidates = new List<Candidate>();
                    foreach (var option in machines)
                    {
                        int maximum = Math.Min(remaining, WorkshopRules.MaximumNativeBatch);
                        maximum = Math.Min(maximum, Math.Max(1, option.Station.Output.Length)
                            * (int)Math.Min(WorkshopRules.MaximumNativeBatch, ItemClass.GetForId(step.Recipe.itemValueType).MaxCount) / step.Recipe.count);
                        int low = 0, high = maximum;
                        Machine after; WorkshopForgePlan feed; double end; string reason;
                        while (low < high)
                        {
                            int amount = low + (high - low + 1) / 2;
                            if (Probe(projection, option.Station, option.Recipe, amount, inputsReady, out after, out feed, out end, out reason)) low = amount;
                            else { high = amount - 1; blocked = reason; }
                        }
                        if (low == 0) continue;
                        Probe(projection, option.Station, option.Recipe, low, inputsReady, out after, out feed, out end, out reason);
                        double start = Math.Max(inputsReady, projection.Machines[option.Station].Available);
                        candidates.Add(new Candidate { Option = option, Capacity = low, Start = start, Rate = Math.Max(.05, (end - start) / low) });
                    }
                    if (candidates.Count == 0) { if (string.IsNullOrEmpty(blocked)) blocked = "waiting for machines"; return false; }
                    Allocate(candidates, remaining);
                    int assigned = 0;
                    foreach (var candidate in candidates.Where(c => c.Assigned > 0))
                    {
                        Machine after; WorkshopForgePlan feed; double end; string reason;
                        var option = candidate.Option;
                        if (!Probe(projection, option.Station, option.Recipe, candidate.Assigned, inputsReady, out after, out feed, out end, out reason)
                            || !Commit(projection, option.Station, option.Recipe, candidate.Assigned, after, feed, end))
                        { blocked = string.IsNullOrEmpty(reason) ? "waiting for shared supplies" : reason; return false; }
                        finish = Math.Max(finish, end + CollectionSeconds);
                        // Root output belongs to this MAKE request; only the
                        // dependency steps supply later steps in the virtual pool.
                        if (!ReferenceEquals(step, plan.Steps[plan.Steps.Count - 1]))
                            projection.Stock.Add(step.Recipe.itemValueType, (long)candidate.Assigned * step.Recipe.count, end + CollectionSeconds);
                        assigned += candidate.Assigned;
                    }
                    if (assigned == 0) { blocked = "waiting for machines"; return false; }
                    remaining -= assigned;
                }
            }
            return true;
        }

        private static void Allocate(List<Candidate> candidates, int required)
        {
            long wanted = Math.Min(required, candidates.Sum(c => (long)c.Capacity));
            double low = 0, high = candidates.Max(c => c.Start + c.Rate * c.Capacity) + .001;
            for (int i = 0; i < 48; i++)
            {
                double mid = (low + high) / 2;
                long possible = candidates.Sum(c => (long)Math.Min(c.Capacity, Math.Max(0, Math.Floor((mid - c.Start) / c.Rate))));
                if (possible >= wanted) high = mid; else low = mid;
            }
            long used = 0;
            foreach (var c in candidates)
            { c.Assigned = Math.Min(c.Capacity, Math.Max(0, (int)Math.Floor((low - c.Start) / c.Rate))); used += c.Assigned; }
            while (used < wanted)
            {
                var next = candidates.Where(c => c.Assigned < c.Capacity).OrderBy(c => c.Start + (c.Assigned + 1) * c.Rate).First();
                next.Assigned++; used++;
            }
        }

        private bool Probe(State projection, TileEntityWorkstation station, Recipe recipe, int batches, double inputReady,
            out Machine after, out WorkshopForgePlan feed, out double end, out string blocked)
        {
            after = projection.Machines[station].Clone(); feed = null; end = 0; blocked = after.Blocked;
            // Keep large/modded recipe graphs from stalling the game UI. Never
            // present a partly evaluated schedule as a complete job estimate.
            if (clock.ElapsedMilliseconds > 20) { blocked = "complex plan"; return false; }
            if (!string.IsNullOrEmpty(blocked) || batches <= 0) return false;
            double start = Math.Max(inputReady, after.Available);
            double workStart = start;
            if (!Finite(start)) { blocked = "waiting for machines"; return false; }
            if (recipe.materialBasedRecipe)
            {
                AdvanceForge(after, start);
                if (!WorkshopForgePlan.Create(station, recipe, batches, after.Input, after.Timers, out feed, out blocked)) return false;
                foreach (var supply in feed.Supplies)
                    start = Math.Max(start, projection.Stock.Take(supply.itemValue.type, supply.count, false));
                if (!Finite(start)) { blocked = "waiting for forge supplies"; return false; }
                // Existing input can continue to smelt while a supplied raw item waits.
                AdvanceForge(after, start);
                if (!WorkshopForgePlan.Create(station, recipe, batches, after.Input, after.Timers, out feed, out blocked)) return false;
                after.Input = ItemStack.Clone(feed.Inputs);
                workStart = start;
                start += feed.Seconds;
                AdvanceForge(after, start + .00001);
                ItemStack missing;
                if (!WorkshopPlanner.ConsumeMaterials(after.Input, station.InputSlotCount, recipe.ingredients, batches, out missing))
                { blocked = "waiting for forge materials"; return false; }
            }
            end = start + Math.Max(.05, recipe.craftingTime) * batches;
            after.WorkFuel = end - workStart;
            if (!Finite(end) || !Fuel(projection.Stock, after, after.WorkFuel, false))
            { blocked = "needs fuel"; return false; }
            AdvanceForge(after, end);
            after.Available = end;
            return true;
        }

        private bool Commit(State projection, TileEntityWorkstation station, Recipe recipe, int batches,
            Machine after, WorkshopForgePlan feed, double finish)
        {
            if (feed != null)
                foreach (var supply in feed.Supplies)
                    if (!Finite(projection.Stock.Take(supply.itemValue.type, supply.count))) return false;
            if (!Fuel(projection.Stock, after, after.WorkFuel, true)) return false;
            projection.Machines[station] = after;
            return true;
        }

        private bool Fuel(Inventory inventory, Machine machine, double seconds, bool consume)
        {
            if (!WorkshopMachines.Uses(machine.Station, TileEntityWorkstation.Module.Fuel)) return true;
            if (!Finite(seconds)) return false;
            if (!controller.AutoFuel && !machine.Station.IsBurning) return false;
            double missing = Math.Max(0, seconds - machine.Fuel);
            if (missing > 0)
            {
                var wood = ItemClass.GetItem("resourceWood", false);
                if (!controller.AutoFuel || wood == null || wood.IsEmpty() || ItemClass.GetFuelValue(wood) <= 0) return false;
                long count = (long)Math.Ceiling(missing / ItemClass.GetFuelValue(wood));
                // Fuel must exist now; production cannot assume a future wood order.
                if (inventory.Take(wood.type, count, false) > 0) return false;
                if (consume)
                { inventory.Take(wood.type, count); machine.Fuel += count * (double)ItemClass.GetFuelValue(wood); }
            }
            if (consume) machine.Fuel = Math.Max(0, machine.Fuel - seconds);
            return true;
        }

        private static void AdvanceForge(Machine machine, double time)
        {
            double elapsed = time - machine.ForgeClock;
            if (!Finite(elapsed) || elapsed <= 0 || !WorkshopMachines.Uses(machine.Station, TileEntityWorkstation.Module.Material_Input)) return;
            int slots = machine.Timers.Length;
            for (int i = 0; i < slots; i++)
            {
                var raw = machine.Input[i];
                if (raw == null || raw.IsEmpty()) continue;
                double first = machine.Timers[i] >= 0 ? machine.Timers[i] : WorkshopForgePlan.SmeltSeconds(machine.Station, raw.itemValue, true);
                double repeat = WorkshopForgePlan.SmeltSeconds(machine.Station, raw.itemValue, false);
                int count = elapsed + .000001 < first ? 0 : (int)Math.Min(raw.count, 1 + Math.Floor((elapsed - first + .000001) / repeat));
                var unit = machine.Input.Skip(slots).FirstOrDefault(s => s != null && !s.itemValue.IsEmpty()
                    && s.itemValue.ItemClass.MadeOfMaterial.ForgeCategory == raw.itemValue.ItemClass.MadeOfMaterial.ForgeCategory);
                if (unit == null) continue;
                count = Math.Min(count, Math.Max(0, unit.itemValue.ItemClass.MaxCount - unit.count) / Math.Max(1, raw.itemValue.ItemClass.GetWeight()));
                unit.count += count * raw.itemValue.ItemClass.GetWeight(); raw.count -= count;
                machine.Timers[i] = raw.count == 0 ? -1 : (float)Math.Max(0, first + count * repeat - elapsed);
                if (raw.count == 0) machine.Input[i] = ItemStack.Empty.Clone();
            }
            machine.ForgeClock = time;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
