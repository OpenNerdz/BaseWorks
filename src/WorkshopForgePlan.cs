using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    // Dry-run the entire input layout before withdrawing anything. Keep each
    // in-flight item in its original lane; unstarted tails can use spare lanes.
    internal sealed class WorkshopForgePlan
    {
        internal ItemStack[] Inputs;
        internal readonly List<ItemStack> Supplies = new List<ItemStack>();
        internal double Seconds;
        private struct Lane
        {
            internal double First, Repeat;
            internal int Count, Weight;
            internal string Category;
        }

        internal static float SmeltSeconds(TileEntityWorkstation station, ItemValue raw, bool first)
        {
            var item = raw.ItemClass;
            float seconds = item.GetWeight() * (item.MeltTimePerUnit > 0 ? item.MeltTimePerUnit : 1f);
            string tag = first ? item.GetItemName() : "unit_" + item.MadeOfMaterial.ForgeCategory;
            if (WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Tools))
                foreach (var tool in station.Tools)
                {
                    if (tool == null || tool.IsEmpty()) continue;
                    float multiplier = 1f;
                    tool.itemValue.ModifyValue(null, null, PassiveEffects.CraftingSmeltTime,
                        ref seconds, ref multiplier, FastTags<TagGroup.Global>.Parse(tag));
                    seconds *= multiplier;
                }
            return float.IsNaN(seconds) || float.IsInfinity(seconds) ? 3600f : Math.Max(.05f, seconds);
        }

        internal static bool Create(TileEntityWorkstation station, Recipe recipe, int batches,
            out WorkshopForgePlan plan, out string reason)
        {
            return Create(station, recipe, batches, station.Input, null, out plan, out reason);
        }

        // Read-only virtual-input overload used by the ETA projection. It never
        // replaces the native station's arrays, queue, clocks or world state.
        internal static bool Create(TileEntityWorkstation station, Recipe recipe, int batches,
            ItemStack[] input, float[] timers, out WorkshopForgePlan plan, out string reason)
        {
            plan = new WorkshopForgePlan { Inputs = ItemStack.Clone(input) };
            reason = "Waiting for smelted materials";
            int slots = Math.Min(station.InputSlotCount, plan.Inputs.Length);
            foreach (var ingredient in recipe.ingredients)
            {
                long available = WorkshopMachines.MaterialUnits(input, slots, ingredient, false);
                long covered = WorkshopMachines.MaterialUnits(input, slots, ingredient, true);
                long required = (long)ingredient.count * batches;
                if (required <= covered) continue;
                string name = WorkshopMachines.RawMaterial(ingredient.itemValue.ItemClass.GetItemName());
                var raw = name == null ? null : ItemClass.GetItem(name, false);
                if (raw == null || raw.IsEmpty() || !station.AcceptsMaterial(raw.ItemClass.MadeOfMaterial))
                { reason = "Supply this forge's special material manually"; return false; }
                int weight = raw.ItemClass.GetWeight();
                long capacity = Math.Max(0, ingredient.itemValue.ItemClass.MaxCount - covered);
                int count = WorkshopRules.FeedCount(required, available, covered - available, weight,
                    weight <= 0 ? 0 : (int)Math.Min(1000, capacity / weight));
                if (count <= 0 || covered + (long)count * weight < required)
                { reason = "Not enough forge material capacity"; return false; }
                plan.Supplies.Add(new ItemStack(raw, count));
            }
            // Reserve one slot for every missing input kind before letting any
            // ingredient use spare lanes. Iron must not take the clay's last slot.
            var remaining = plan.Supplies.Select(s => s.count).ToArray();
            for (int n = 0; n < plan.Supplies.Count; n++)
            {
                var supply = plan.Supplies[n];
                if (plan.Inputs.Take(slots).Any(s => s != null && !s.IsEmpty()
                    && StorageTransferPlan.Matches(s, supply) && s.count < s.itemValue.ItemClass.MaxCount)) continue;
                int slot = Array.FindIndex(plan.Inputs, 0, slots, s => s == null || s.IsEmpty());
                if (slot < 0) { reason = "Free a forge input slot for " + Localization.Get(supply.itemValue.ItemClass.GetItemName()); return false; }
                plan.Inputs[slot] = new ItemStack(supply.itemValue.Clone(), 1);
                remaining[n]--;
            }
            var loads = new double[slots];
            for (int i = 0; i < slots; i++) loads[i] = LaneSeconds(station, plan.Inputs[i], input, timers, i);
            // Longest material workload first; free lanes go where they reduce
            // completion time most. Bounded feed limits keep this loop small.
            var supplies = plan.Supplies;
            foreach (int n in Enumerable.Range(0, remaining.Length).OrderByDescending(n =>
                remaining[n] * SmeltSeconds(station, supplies[n].itemValue, false)))
            {
                var supply = plan.Supplies[n];
                while (remaining[n] > 0)
                {
                    int best = -1; double finish = double.PositiveInfinity;
                    for (int i = 0; i < slots; i++)
                    {
                        var stack = plan.Inputs[i];
                        bool empty = stack == null || stack.IsEmpty();
                        if (!empty && (!StorageTransferPlan.Matches(stack, supply) || stack.count >= supply.itemValue.ItemClass.MaxCount)) continue;
                        double next = loads[i] + SmeltSeconds(station, supply.itemValue, empty);
                        if (next < finish) { best = i; finish = next; }
                    }
                    if (best < 0) { reason = "Free forge input space"; return false; }
                    int existing = plan.Inputs[best] == null || plan.Inputs[best].IsEmpty() ? 0 : plan.Inputs[best].count;
                    int chunk = Math.Min(remaining[n], supply.itemValue.ItemClass.MaxCount - existing);
                    if (existing == 0) plan.Inputs[best] = new ItemStack(supply.itemValue.Clone(), chunk);
                    else plan.Inputs[best].count += chunk;
                    loads[best] = finish + (chunk - 1) * (double)SmeltSeconds(station, supply.itemValue, false);
                    remaining[n] -= chunk;
                }
            }
            BalanceTails(station, plan.Inputs, input, timers, slots);
            var lanes = new Lane[slots];
            for (int i = 0; i < slots; i++)
            {
                var stack = plan.Inputs[i];
                if (stack == null || stack.IsEmpty()) continue;
                lanes[i] = new Lane { First = FirstSeconds(station, stack, input, timers, i),
                    Repeat = SmeltSeconds(station, stack.itemValue, false), Count = stack.count,
                    Weight = stack.itemValue.ItemClass.GetWeight(), Category = stack.itemValue.ItemClass.MadeOfMaterial.ForgeCategory };
                loads[i] = lanes[i].First + (stack.count - 1) * lanes[i].Repeat;
            }
            // Estimate readiness for this batch, not the time to empty unrelated
            // or overstocked input stacks. Each native lane has its own timer.
            foreach (var ingredient in recipe.ingredients)
            {
                long deficit = (long)ingredient.count * batches - WorkshopMachines.MaterialUnits(input, slots, ingredient, false);
                if (deficit <= 0) continue;
                string category = ingredient.itemValue.ItemClass.MadeOfMaterial.ForgeCategory;
                double low = 0, high = loads.Length == 0 ? 0 : loads.Max();
                for (int step = 0; step < 32; step++)
                {
                    double mid = (low + high) / 2; long units = 0;
                    for (int i = 0; i < slots; i++)
                    {
                        var lane = lanes[i];
                        if (lane.Count <= 0 || !string.Equals(category, lane.Category, StringComparison.OrdinalIgnoreCase)) continue;
                        if (mid >= lane.First) units += Math.Min(lane.Count, 1 + (long)((mid - lane.First) / lane.Repeat)) * lane.Weight;
                    }
                    if (units >= deficit) high = mid; else low = mid;
                }
                plan.Seconds = Math.Max(plan.Seconds, high);
            }
            return true;
        }

        // Move whole unstarted items only when doing so reduces the two lanes'
        // finish time. Transfer a calculated chunk, not one iteration per item.
        private static void BalanceTails(TileEntityWorkstation station, ItemStack[] after,
            ItemStack[] before, float[] timers, int slots)
        {
            for (int pass = 0; pass < slots * slots * 4; pass++)
            {
                int from = -1, to = -1, count = 0;
                double improvement = .001;
                for (int a = 0; a < slots; a++)
                {
                    var source = after[a];
                    if (source == null || source.IsEmpty() || source.count < 2) continue;
                    double repeat = SmeltSeconds(station, source.itemValue, false);
                    double sourceTime = LaneSeconds(station, source, before, timers, a);
                    for (int b = 0; b < slots; b++)
                    {
                        if (a == b) continue;
                        var destination = after[b];
                        bool empty = destination == null || destination.IsEmpty();
                        if (!empty && !StorageTransferPlan.Matches(source, destination)) continue;
                        int capacity = source.itemValue.ItemClass.MaxCount - (empty ? 0 : destination.count);
                        int maximum = Math.Min(source.count - 1, capacity);
                        if (maximum <= 0) continue;
                        double destinationTime = empty ? 0 : LaneSeconds(station, destination, before, timers, b);
                        double offset = empty ? SmeltSeconds(station, source.itemValue, true) - repeat : 0;
                        double ideal = (sourceTime - destinationTime - offset) / (2 * repeat);
                        for (int round = 0; round < 2; round++)
                        {
                            int moved = Math.Min(maximum, Math.Max(1, (int)Math.Floor(ideal) + round));
                            double gain = Math.Max(sourceTime, destinationTime)
                                - Math.Max(sourceTime - moved * repeat, destinationTime + offset + moved * repeat);
                            if (gain <= improvement) continue;
                            improvement = gain; from = a; to = b; count = moved;
                        }
                    }
                }
                if (from < 0) return;
                if (after[to] == null || after[to].IsEmpty()) after[to] = new ItemStack(after[from].itemValue.Clone(), count);
                else after[to].count += count;
                after[from].count -= count;
            }
        }

        private static double FirstSeconds(TileEntityWorkstation station, ItemStack stack, ItemStack[] input, float[] timers, int slot)
        {
            var before = input[slot];
            float timer = timers == null ? station.GetTimerForSlot(slot) : timers[slot];
            return before != null && !before.IsEmpty() && before.itemValue.type == stack.itemValue.type
                && timer >= 0 && !float.IsInfinity(timer) && !float.IsNaN(timer)
                ? timer : SmeltSeconds(station, stack.itemValue, true);
        }

        private static double LaneSeconds(TileEntityWorkstation station, ItemStack stack, ItemStack[] input, float[] timers, int slot)
        {
            return stack == null || stack.IsEmpty() ? 0 : FirstSeconds(station, stack, input, timers, slot)
                + Math.Max(0, stack.count - 1) * (double)SmeltSeconds(station, stack.itemValue, false);
        }
    }
}
