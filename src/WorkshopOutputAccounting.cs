using System;
using System.Collections.Generic;

namespace NearbyCraft
{
    internal static class WorkshopOutputAccounting
    {
        internal static Dictionary<int, int> Capture(ItemStack[] output)
        {
            var counts = new Dictionary<int, int>();
            foreach (var stack in output)
            {
                if (stack == null || stack.IsEmpty()) continue;
                int count;
                counts.TryGetValue(stack.itemValue.type, out count);
                counts[stack.itemValue.type] = checked(count + stack.count);
            }
            return counts;
        }

        // Only a decrease in the real output inventory is a collection. Moving
        // stacks between slots or cancelling a native queue cannot earn credit.
        internal static bool CreditRemoved(WorkshopControllerData controller, Dictionary<int, int> before, ItemStack[] after)
        {
            bool changed = false;
            foreach (var pair in before)
            {
                int left = 0;
                foreach (var stack in after)
                    if (stack != null && !stack.IsEmpty() && stack.itemValue.type == pair.Key) left += stack.count;
                int removed = Math.Max(0, pair.Value - left);
                if (removed > 0)
                    changed |= WorkshopStore.CreditDelivery(controller, ItemClass.GetForId(pair.Key).GetItemName(), removed, true);
            }
            return changed;
        }
    }
}
