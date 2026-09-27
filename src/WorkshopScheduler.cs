using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    // One scheduler per network tick. Native queues and inventories remain the
    // source of truth; persisted leases describe only which forge is preparing
    // which future batch. They are included in capacity/demand reservations.
    internal sealed class WorkshopScheduler
    {
        private readonly StorageNetworkSession network;
        private readonly EntityPlayerLocal player;
        private readonly XUi xui;
        private readonly WorkshopControllerData controller;
        private readonly List<TileEntityWorkstation> stations;
        private readonly Dictionary<string, ILookup<int, Recipe>> recipes = new Dictionary<string, ILookup<int, Recipe>>();
        private readonly Dictionary<string, string> reasons = new Dictionary<string, string>();
        private readonly Dictionary<WorkshopTarget, WorkshopProductionPlan> plans = new Dictionary<WorkshopTarget, WorkshopProductionPlan>();
        private readonly Dictionary<string, string> planningStatus = new Dictionary<string, string>();
        private readonly Dictionary<TileEntityWorkstation, Dictionary<Recipe, Recipe>> prepared = new Dictionary<TileEntityWorkstation, Dictionary<Recipe, Recipe>>();
        private WorkshopReservationBank reservations;
        private WorkshopProductionPlan activePlan;
        private int actions;

        private sealed class Candidate
        {
            internal TileEntityWorkstation Station;
            internal Recipe Recipe;
            internal int Ready, Covered;
            internal double Fit;
            internal double FirstFinish, SecondsPerBatch;
            internal int Capacity, Assigned;
        }

        internal WorkshopScheduler(StorageNetworkSession network, EntityPlayerLocal player, XUi xui,
            WorkshopControllerData controller, List<TileEntityWorkstation> stations)
        { this.network = network; this.player = player; this.xui = xui; this.controller = controller; this.stations = stations; }

        internal void Run()
        {
            ServiceAssignments();
            BuildPlans();
            // Every order gets a first opportunity before an earlier large
            // order can occupy the spare machines in the second pass.
            for (int pass = 0; pass < 2 && WorkshopStore.Writable; pass++)
                foreach (var target in controller.Targets)
                {
                    if (!target.Enabled || !WorkshopStore.Writable) continue;
                    long demand = Demand(target);
                    if (demand <= 0) continue;
                    WorkshopProductionPlan plan;
                    if (!plans.TryGetValue(target, out plan) || plan.Maximum <= 0) continue;
                    activePlan = plan;
                    actions = pass == 0 ? 1 : WorkshopRules.MaximumStations;
                    string reason;
                    Ensure(target, ItemClass.GetItem(target.Item, false).type, Math.Min(demand, plan.Maximum + OwnLeased(target)), target.Once,
                        new HashSet<int>(), out reason);
                    if (!string.IsNullOrEmpty(reason)) reasons[target.Item] = reason;
                }
            activePlan = null;
            bool completed = false;
            foreach (var target in controller.Targets)
            {
                if (target.Once && target.TrackDelivery && target.CompletedUtcTicks == 0 && target.Remaining == 0
                    && target.Queued > 0 && target.Returned >= target.Queued
                    && !controller.Smelting.Any(j => j.Owner == target.Item))
                {
                    var item = ItemClass.GetItem(target.Item, false);
                    if (item != null && !item.IsEmpty() && Pending(item.type) == 0)
                    { WorkshopStore.RecordCompletion(controller, target, true); completed = true; }
                }
                Describe(target);
            }
            if (completed) Save();
        }

        private void BuildPlans()
        {
            plans.Clear(); planningStatus.Clear(); reservations = new WorkshopReservationBank();
            var pending = new List<WorkshopTarget>();
            var outputs = new List<int>();
            foreach (var target in controller.Targets)
            {
                if (!target.Enabled || Demand(target) <= OwnLeased(target)) continue;
                var item = ItemClass.GetItem(target.Item, false);
                if (item == null || item.IsEmpty()) continue;
                pending.Add(target);
                outputs.Add(item.type);
            }
            if (pending.Count == 0) return;
            var planner = new WorkshopProductionPlanner(network, player, xui, controller, stations,
                controller.AutoCraft, outputs, Prepare);
            var pool = planner.CreatePool();
            foreach (var target in pending)
            {
                long demand = Demand(target) - OwnLeased(target);
                if (demand <= 0) continue;
                var item = ItemClass.GetItem(target.Item, false);
                int requested = (int)Math.Min(WorkshopRules.MaximumTarget, demand);
                var plan = planner.Plan(item.type, requested, pool, reservations);
                plans[target] = plan;
                if (plan.Maximum < requested)
                    planningStatus[target.Item] = "Can make " + plan.Maximum + " of " + requested + " now"
                        + (string.IsNullOrEmpty(plan.Missing) ? "" : " / Missing " + plan.Missing);
            }
        }

        private long Demand(WorkshopTarget target)
        {
            var item = ItemClass.GetItem(target.Item, false);
            if (item == null || item.IsEmpty()) return 0;
            return target.Once ? target.Remaining : target.Target - network.CountProduct(item.type) - Pending(item.type);
        }

        private long OwnLeased(WorkshopTarget target)
        { return controller.Smelting.Where(j => j.Owner == target.Item && j.Item == target.Item).Sum(j => (long)j.Count); }

        private Recipe Prepare(Recipe recipe, TileEntityWorkstation station)
        {
            Dictionary<Recipe, Recipe> byRecipe;
            if (!prepared.TryGetValue(station, out byRecipe)) prepared[station] = byRecipe = new Dictionary<Recipe, Recipe>();
            Recipe staged;
            if (!byRecipe.TryGetValue(recipe, out staged)) byRecipe[recipe] = staged = WorkshopManager.PrepareRecipe(recipe, xui, station);
            return staged;
        }

        private long Pending(int type)
        { return stations.Sum(s => WorkshopManager.CountOutput(s, type) + WorkshopManager.CountQueued(s, type)); }

        private bool Save()
        {
            string error;
            if (WorkshopStore.SaveProgress(out error)) return true;
            WorkshopManager.Status[controller.Position] = error;
            actions = 0;
            return false;
        }

        private IEnumerable<Recipe> Recipes(TileEntityWorkstation station, int type)
        {
            string name = station.block.GetBlockName();
            ILookup<int, Recipe> list;
            if (!recipes.TryGetValue(name, out list))
                recipes[name] = list = XUiM_Recipes.FilterRecipesByWorkstation(name, XUiM_Recipes.GetRecipes())
                    .Where(WorkshopManager.Productive).ToLookup(r => r.itemValueType);
            return list[type];
        }

        private void ServiceAssignments()
        {
            foreach (var job in controller.Smelting.ToArray())
            {
                if (!WorkshopStore.Writable) return;
                var owner = controller.Targets.Find(t => t.Item == job.Owner);
                var station = stations.Find(s => s.ToWorldPos() == job.Position);
                var item = ItemClass.GetItem(job.Item, false);
                if (owner == null || !owner.Enabled || Demand(owner) <= 0 || station == null || item == null || item.IsEmpty()
                    || (job.Item != job.Owner && !controller.AutoCraft))
                { controller.Smelting.Remove(job); if (!Save()) return; continue; }
                string reason = "Recipe changed; assigning again";
                var recipe = Recipes(station, item.type).Where(r => WorkshopManager.RecipeUsable(r, station, player, out reason))
                    .Select(r => Prepare(r, station)).FirstOrDefault(r => r.materialBasedRecipe && WorkshopManager.RecipeKey(r) == job.RecipeKey);
                // Never reinterpret an old allocation as a different recipe.
                if (recipe == null || checked(recipe.count * job.Batches) != job.Count)
                { controller.Smelting.Remove(job); if (!Save()) return; reasons[job.Owner] = reason; continue; }
                if (job.Item == job.Owner)
                {
                    int cap = owner.Once ? WorkshopRules.OrderBatches((int)Math.Min(WorkshopRules.MaximumTarget, Demand(owner)), recipe.count)
                        : WorkshopRules.BatchesNeeded((int)Math.Min(WorkshopRules.MaximumTarget, Demand(owner)), 0, 0, 0, recipe.count);
                    if (cap < job.Batches)
                    {
                        job.Batches = cap; job.Count = cap * recipe.count;
                        if (cap == 0) controller.Smelting.Remove(job);
                        if (!Save()) return;
                        if (cap == 0) continue;
                    }
                }
                // Keep the unqueued remainder attached while a rolling chunk is
                // crafting. The raw lanes keep smelting and another ready chunk
                // is queued as soon as the native queue becomes free.
                int covered = WorkshopMachines.MaterialBatches(station, recipe, true);
                if (covered < job.Batches || WorkshopMachines.NeedsInputBalance(station))
                    network.FeedForge(station, recipe, job.Batches, controller.AutoFuel, out reason);
                if (station.hasRecipeInQueue())
                {
                    int nativeQueued = (int)Math.Min(int.MaxValue, WorkshopManager.CountQueued(station, item.type));
                    reason = "Crafting " + Localization.Get(job.Item) + " x" + nativeQueued
                        + " / next " + job.Count + " still smelting";
                    WorkshopManager.StationStatus[job.Position] = reasons[job.Owner] = reason;
                    continue;
                }
                ItemStack missing = null;
                int ready = Math.Min(job.Batches, WorkshopMachines.MaterialBatches(station, recipe, false));
                int queuedBatches = ready <= 0 ? 0 : network.QueueLargestBatch(station, Batch(recipe, ready),
                    ReservedOutputs(stations, controller, job), controller.AutoFuel, out missing, out reason);
                if (queuedBatches > 0)
                {
                    int queuedCount = checked(queuedBatches * recipe.count);
                    job.Batches -= queuedBatches;
                    job.Count -= queuedCount;
                    if (job.Batches == 0) controller.Smelting.Remove(job);
                    if (owner.Once && job.Item == owner.Item)
                    { owner.Remaining = Math.Max(0, owner.Remaining - queuedCount); owner.Queued = checked(owner.Queued + queuedCount); }
                    if (!Save()) return;
                    reasons[job.Owner] = "Crafting " + Localization.Get(job.Item) + " x" + queuedCount
                        + (job.Count > 0 ? " / " + job.Count + " still smelting" : "");
                    WorkshopManager.StationStatus[job.Position] = reasons[job.Owner];
                    continue;
                }
                if ((ready == 0 || missing != null) && covered >= job.Batches)
                    reason = "Smelting for " + Localization.Get(job.Item) + " x" + job.Count;
                WorkshopManager.StationStatus[job.Position] = reason;
                reasons[job.Owner] = reason;
            }
        }

        private bool Ensure(WorkshopTarget owner, int type, long deficit, bool roundUp, HashSet<int> path, out string reason)
        {
            reason = "Waiting for a free machine";
            if (actions <= 0 || !WorkshopStore.Writable) return false;
            if (path.Count >= 8 || !path.Add(type)) { reason = "Recipe dependency cycle or depth limit"; return false; }
            try
            {
                bool root = type == ItemClass.GetItem(owner.Item, false).type;
                long leased = controller.Smelting.Where(j => ItemClass.GetItem(j.Item, false).type == type
                    && (!root || j.Owner == owner.Item)).Sum(j => (long)j.Count);
                long need = deficit - leased;
                if (need <= 0) { reason = "Smelting assigned materials"; return true; }
                var candidates = new List<Candidate>();
                var dependencyRecipes = new List<Recipe>();
                // Candidate timing can probe the same ingredients many times
                // (including binary searches for the largest safe batch). Use
                // one live storage view for this comparison only. Transactions
                // and dependency decisions below still read/validate live slots.
                Dictionary<int, long> available = null;
                bool matching = false;
                foreach (var station in stations)
                {
                    if (station.IsUserAccessing()) continue;
                    foreach (var recipe in Recipes(station, type))
                    {
                        matching = true;
                        if (!WorkshopManager.RecipeUsable(recipe, station, player, out reason)) continue;
                        bool busy = station.hasRecipeInQueue() || controller.Smelting.Any(j => j.Position == station.ToWorldPos());
                        // A busy machine cannot accept a batch. Only stage a
                        // non-forge recipe when its ingredients may still be
                        // produced by other machines in AutoCraft mode.
                        if (busy && (!controller.AutoCraft || recipe.materialBasedRecipe)) continue;
                        var staged = Prepare(recipe, station);
                        if (activePlan != null && !activePlan.Allows(type, staged)) continue;
                        if (controller.AutoCraft && !staged.materialBasedRecipe
                            && !dependencyRecipes.Any(r => WorkshopManager.RecipeKey(r) == WorkshopManager.RecipeKey(staged)))
                            dependencyRecipes.Add(staged);
                        if (busy) continue;
                        var candidate = new Candidate { Station = station, Recipe = staged,
                            Ready = staged.materialBasedRecipe ? WorkshopMachines.MaterialBatches(station, staged, false) : 0,
                            Covered = staged.materialBasedRecipe ? WorkshopMachines.MaterialBatches(station, staged, true) : 0,
                            Fit = staged.materialBasedRecipe ? WorkshopMachines.MaterialFit(station, staged) : 0 };
                        int maximum = roundUp ? WorkshopRules.OrderBatches((int)Math.Min(WorkshopRules.MaximumTarget, need), staged.count)
                            : WorkshopRules.BatchesNeeded((int)Math.Min(WorkshopRules.MaximumTarget, need), 0, 0, 0, staged.count);
                        if (available == null) available = network.SnapshotProductCounts(true);
                        candidate.FirstFinish = Estimate(station, staged, 1, available);
                        candidate.Capacity = LargestFiniteBatch(station, staged, maximum, candidate.FirstFinish, available);
                        if (candidate.Capacity > 0)
                            candidate.SecondsPerBatch = Estimate(station, staged, candidate.Capacity, available) / candidate.Capacity;
                        candidates.Add(candidate);
                    }
                }
                if (!matching) reason = "Supply " + need + " " + Localization.Get(ItemClass.GetForId(type).GetItemName()) + " or add its crafting machine";
                // Estimate native completion times; material affinity breaks ties.
                candidates = candidates.Where(c => roundUp || c.Recipe.count <= need)
                    .OrderBy(c => c.FirstFinish / Math.Min(need, Math.Max(1, c.Recipe.count)))
                    .ThenByDescending(c => c.Ready > 0).ThenByDescending(c => c.Covered > 0)
                    .ThenByDescending(c => c.Fit).ThenBy(c => WorkshopMachines.HasSmeltingInput(c.Station) ? 1 : 0)
                    .GroupBy(c => c.Station).Select(g => g.First()).ToList();
                Allocate(candidates, need, roundUp);
                // Retain unavailable candidates for precise transactional errors.
                candidates = candidates.OrderBy(c => c.Assigned > 0 ? 0 : 1)
                    .ThenBy(c => c.Assigned > 0 ? c.SecondsPerBatch * c.Assigned : c.FirstFinish).ToList();
                bool progress = leased > 0;
                for (int index = 0; index < candidates.Count && need > 0 && actions > 0 && WorkshopStore.Writable; index++)
                {
                    var candidate = candidates[index]; var station = candidate.Station; var recipe = candidate.Recipe;
                    // Earlier dependency work may have taken this station.
                    if (station.hasRecipeInQueue() || controller.Smelting.Any(j => j.Position == station.ToWorldPos())) continue;
                    int max = roundUp ? WorkshopRules.OrderBatches((int)Math.Min(WorkshopRules.MaximumTarget, need), recipe.count)
                        : WorkshopRules.BatchesNeeded((int)Math.Min(WorkshopRules.MaximumTarget, need), 0, 0, 0, recipe.count);
                    int share = candidate.Assigned > 0 ? Math.Min(max, candidate.Assigned)
                        : double.IsInfinity(candidate.FirstFinish) ? Math.Min(max, 1) : 0;
                    if (share == 0) { reason = "Less than one recipe yield needed"; continue; }
                    ItemStack missing = null;
                    bool queued = false;
                    var claim = activePlan == null ? null : activePlan.Claim(recipe);
                    int amount = network.QueueLargestBatch(station, Batch(recipe, share), ReservedOutputs(stations, controller),
                        controller.AutoFuel, reservations, claim, out missing, out reason);
                    if (amount > 0)
                    {
                        int count = amount * recipe.count;
                        if (root && owner.Once) { owner.Remaining = Math.Max(0, owner.Remaining - count); owner.Queued = checked(owner.Queued + count); if (!Save()) return true; }
                        need -= count; actions--; progress = queued = true;
                        WorkshopManager.StationStatus[station.ToWorldPos()] = "Crafting " + Localization.Get(recipe.GetName()) + " x" + count;
                        reason = WorkshopManager.StationStatus[station.ToWorldPos()];
                    }
                    if (queued || missing == null) continue;
                    if (recipe.materialBasedRecipe)
                    {
                        amount = LargestReservableBatch(type, recipe.count, share, ReservedOutputs(stations, controller));
                        if (amount > 0)
                        {
                            amount = network.FeedLargestForge(station, recipe, amount, controller.AutoFuel,
                                reservations, claim, out reason);
                            if (amount <= 0) continue;
                            int count = checked(recipe.count * amount);
                            var p = station.ToWorldPos();
                            controller.Smelting.Add(new WorkshopSmeltAssignment { X = p.x, Y = p.y, Z = p.z,
                                Item = recipe.GetName(), Owner = owner.Item, Batches = amount, Count = count, RecipeKey = WorkshopManager.RecipeKey(recipe) });
                            if (!Save()) return true;
                            need -= count; actions--; progress = true;
                            reason = "Smelting for " + Localization.Get(recipe.GetName()) + " x" + count;
                            WorkshopManager.StationStatus[p] = reason;
                        }
                        else reason = "Make room in connected storage";
                        continue;
                    }
                }
                // A busy parent machine must not hide the recipe's other inputs.
                // A single mixer making sand can still ask spare forges for cement.
                if (need > 0 && actions > 0 && controller.AutoCraft && WorkshopStore.Writable)
                foreach (var recipe in dependencyRecipes.OrderBy(r => r.craftingTime / Math.Max(1, r.count)).Take(1))
                {
                    int batches = roundUp ? WorkshopRules.OrderBatches((int)Math.Min(WorkshopRules.MaximumTarget, need), recipe.count)
                        : WorkshopRules.BatchesNeeded((int)Math.Min(WorkshopRules.MaximumTarget, need), 0, 0, 0, recipe.count);
                    string blocked = null;
                    foreach (var ingredient in recipe.ingredients)
                    {
                        long missingStored = (long)ingredient.count * batches - network.CountProduct(ingredient.itemValue.type, true);
                        long pendingInput = Pending(ingredient.itemValue.type);
                        long inputNeed = missingStored - pendingInput;
                        if (inputNeed <= 0)
                        {
                            if (missingStored > 0 && pendingInput > 0)
                            {
                                progress = true;
                                reason = "Preparing " + Localization.Get(ingredient.itemValue.ItemClass.GetItemName())
                                    + " / waiting for " + missingStored + " already in machines";
                            }
                            continue;
                        }
                        string dependency;
                        if (Ensure(owner, ingredient.itemValue.type, inputNeed, true, path, out dependency)) progress = true;
                        else if (blocked == null) blocked = dependency;
                        reason = "Preparing " + Localization.Get(ingredient.itemValue.ItemClass.GetItemName()) + " / " + dependency;
                    }
                    if (blocked != null && !progress) reason = blocked;
                }
                return progress;
            }
            finally { path.Remove(type); }
        }

        private int LargestFiniteBatch(TileEntityWorkstation station, Recipe recipe, int maximum, double firstFinish,
            Dictionary<int, long> available)
        {
            if (maximum <= 0 || double.IsInfinity(firstFinish) || double.IsNaN(firstFinish)) return 0;
            if (maximum == 1) return 1;
            double finish = Estimate(station, recipe, maximum, available);
            if (!double.IsInfinity(finish) && !double.IsNaN(finish)) return maximum;
            int low = 1, high = maximum - 1;
            while (low < high)
            {
                int amount = low + (high - low + 1) / 2;
                finish = Estimate(station, recipe, amount, available);
                if (!double.IsInfinity(finish) && !double.IsNaN(finish)) low = amount;
                else high = amount - 1;
            }
            return low;
        }

        // Allocate by estimated completion time without walking every recipe
        // cycle. The binary makespan search is constant-cost even for 100,000
        // item requests; only a small remainder is assigned individually.
        private static void Allocate(List<Candidate> candidates, long need, bool roundUp)
        {
            foreach (var candidate in candidates) candidate.Assigned = 0;
            var available = candidates.Where(c => c.Capacity > 0 && c.SecondsPerBatch > 0
                && !double.IsInfinity(c.SecondsPerBatch) && !double.IsNaN(c.SecondsPerBatch)).ToList();
            if (available.Count == 0 || need <= 0) return;
            long total = available.Sum(c => (long)c.Capacity * c.Recipe.count);
            if (total <= need)
            {
                foreach (var candidate in available) candidate.Assigned = candidate.Capacity;
                return;
            }
            double low = 0;
            double high = available.Max(c => c.SecondsPerBatch * c.Capacity) * 1.000000000001d;
            for (int step = 0; step < 56; step++)
            {
                double middle = (low + high) / 2d;
                if (ProductsAt(available, middle) >= need) high = middle; else low = middle;
            }
            long assignedProducts = 0;
            foreach (var candidate in available)
            {
                candidate.Assigned = Math.Min(candidate.Capacity,
                    Math.Max(0, (int)Math.Floor(low / candidate.SecondsPerBatch)));
                assignedProducts += (long)candidate.Assigned * candidate.Recipe.count;
            }
            while (assignedProducts < need)
            {
                long remaining = need - assignedProducts;
                var next = available.Where(c => c.Assigned < c.Capacity
                        && (roundUp || c.Recipe.count <= remaining))
                    .OrderBy(c => (c.Assigned + 1) * c.SecondsPerBatch).FirstOrDefault();
                if (next == null) break;
                next.Assigned++;
                assignedProducts += next.Recipe.count;
            }
        }

        private static long ProductsAt(IEnumerable<Candidate> candidates, double seconds)
        {
            long products = 0;
            foreach (var candidate in candidates)
            {
                int batches = Math.Min(candidate.Capacity,
                    Math.Max(0, (int)Math.Floor(seconds / candidate.SecondsPerBatch)));
                products += (long)batches * candidate.Recipe.count;
            }
            return products;
        }

        private int LargestReservableBatch(int type, int yield, int maximum, IList<ItemStack> reserved)
        {
            if (maximum <= 0 || yield <= 0) return 0;
            if (!network.CanReserveProducts(reserved, new ItemStack(new ItemValue(type), yield))) return 0;
            if (maximum == 1) return 1;
            int count = checked(yield * maximum);
            if (network.CanReserveProducts(reserved, new ItemStack(new ItemValue(type), count))) return maximum;
            int low = 1, high = maximum - 1;
            while (low < high)
            {
                int amount = low + (high - low + 1) / 2;
                count = checked(yield * amount);
                if (network.CanReserveProducts(reserved, new ItemStack(new ItemValue(type), count))) low = amount;
                else high = amount - 1;
            }
            return low;
        }

        private static long Available(Dictionary<int, long> counts, int type)
        {
            long count;
            return counts.TryGetValue(type, out count) ? count : 0;
        }

        private double Estimate(TileEntityWorkstation station, Recipe recipe, int amount,
            Dictionary<int, long> available)
        {
            if (WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Fuel))
            {
                var wood = ItemClass.GetItem("resourceWood", false);
                if (!controller.AutoFuel && !station.IsBurning) return double.PositiveInfinity;
                if (station.BurnTotalTimeLeft <= 0 && (!controller.AutoFuel || wood == null || wood.IsEmpty()
                    || Available(available, wood.type) == 0)) return double.PositiveInfinity;
            }
            int count = checked(recipe.count * amount);
            if (WorkshopMachines.OutputRoom(station, recipe) < count) return double.PositiveInfinity;
            double seconds = Math.Max(.05, recipe.craftingTime) * amount;
            if (!recipe.materialBasedRecipe)
                return recipe.ingredients.All(i => Available(available, i.itemValue.type) >= (long)i.count * amount)
                    ? seconds : double.PositiveInfinity;
            WorkshopForgePlan plan; string reason;
            if (!WorkshopForgePlan.Create(station, recipe, amount, out plan, out reason)
                || plan.Supplies.Any(s => Available(available, s.itemValue.type) < s.count)) return double.PositiveInfinity;
            // Ignore sub-millisecond binary-search noise when breaking ties.
            return Math.Round(seconds + plan.Seconds, 3);
        }

        private static RecipeQueueItem Batch(Recipe recipe, int amount)
        {
            return new RecipeQueueItem { Recipe = recipe, Multiplier = (short)amount, OneItemCraftTime = recipe.craftingTime,
                CraftingTimeLeft = recipe.craftingTime, IsCrafting = true,
                Quality = (byte)Math.Max(0, Math.Min(byte.MaxValue, recipe.craftingTier)),
                StartingEntityId = GameManager.Instance.World.GetPrimaryPlayer().entityId };
        }

        internal static List<ItemStack> ReservedOutputs(List<TileEntityWorkstation> stations,
            WorkshopControllerData controller, WorkshopSmeltAssignment except = null)
        {
            var result = WorkshopManager.OutstandingOutputs(stations);
            foreach (var job in controller.Smelting)
            {
                if (ReferenceEquals(job, except)) continue;
                var item = ItemClass.GetItem(job.Item, false);
                if (item != null && !item.IsEmpty()) result.Add(new ItemStack(item, job.Count));
            }
            return result;
        }

        private void Describe(WorkshopTarget target)
        {
            string key = WorkshopManager.TargetKey(controller.Position, target.Item);
            var item = ItemClass.GetItem(target.Item, false);
            if (item == null || item.IsEmpty()) { WorkshopManager.TargetStatus[key] = "Item no longer exists"; return; }
            long queued = stations.Sum(s => WorkshopManager.CountQueued(s, item.type));
            long output = stations.Sum(s => WorkshopManager.CountOutput(s, item.type));
            long pending = queued + output;
            string state;
            if (!target.Enabled) state = "Paused";
            else if (target.Once && target.Remaining == 0) state = pending > 0 ? "Crafting / " + pending + " still in machines"
                : target.CollectedManually > 0 ? "Done / collected (" + target.CollectedManually + " manually)" : "Done / returned to storage";
            else if (!target.Once && Demand(target) <= 0) state = pending > 0 ? "Crafting / " + pending + " still in machines" : "In stock / automatic top-up ready";
            else
            {
                string next;
                if (!reasons.TryGetValue(target.Item, out next)) next = "Waiting for a free machine";
                state = DescribeActiveProgress(queued, output, target.Remaining, next);
            }
            var preparing = controller.Smelting.Where(j => j.Owner == target.Item).ToList();
            if (preparing.Count > 0)
            {
                long rolling = preparing.Sum(j =>
                {
                    var station = stations.Find(s => s.ToWorldPos() == j.Position);
                    var product = ItemClass.GetItem(j.Item, false);
                    return station == null || product == null || product.IsEmpty() ? 0 : WorkshopManager.CountQueued(station, product.type);
                });
                string preparation = "Producing / " + preparing.Count + " forge" + (preparing.Count == 1 ? "" : "s") + " / "
                    + string.Join(", ", preparing.Select(j => Localization.Get(j.Item)).Distinct()) + ": "
                    + (rolling > 0 ? rolling + " crafting + " : "") + preparing.Sum(j => j.Count) + " smelting";
                state = queued > 0 || output > 0 ? DescribeActiveProgress(queued, output, target.Remaining, preparation) : preparation;
            }
            string planStatus;
            if (planningStatus.TryGetValue(target.Item, out planStatus) && !string.IsNullOrEmpty(planStatus))
                state = string.IsNullOrEmpty(state) || state == "Waiting for a free machine" ? planStatus : state + " / " + planStatus;
            WorkshopManager.TargetStatus[key] = state;
        }

        internal static string DescribeActiveProgress(long queued, long output, long remaining, string next)
        {
            var parts = new List<string>();
            if (queued > 0) parts.Add("Crafting " + queued + " item" + (queued == 1 ? "" : "s"));
            if (output > 0) parts.Add(output + " finished awaiting collection");
            if (remaining > 0)
            {
                if (string.IsNullOrEmpty(next) || next == "Waiting for a free machine")
                    parts.Add(queued > 0 ? "Next batch starts when the current machine is free"
                        : output > 0 ? "Next batch starts after output collection" : "Waiting for a free machine");
                else if (!next.StartsWith("Crafting ")) parts.Add((queued > 0 || output > 0 ? "Next: " : "") + next);
            }
            return parts.Count == 0 ? (string.IsNullOrEmpty(next) ? "Waiting for a free machine" : next)
                : string.Join(" / ", parts);
        }
    }
}
