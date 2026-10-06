namespace NearbyCraft
{
    // Since 7 Days to Die 3.3, chests, vehicle bags and drone bags keep their slots in an
    // ItemStackGrid that owns each ItemStack and marks the container modified when one changes.
    // Replacing an array entry would orphan that binding, so grid slots are overwritten in place.
    internal static class GameSlots
    {
        internal static void Replace(ItemStack[] live, int index, ItemStack value)
        {
            ItemStack slot = live[index];
            if (slot == null || slot.owner == null)
            {
                live[index] = value;
                return;
            }
            if (value == null || value.count <= 0 || value.IsEmpty()) slot.Set(ItemValue.None, 0);
            else slot.Set(value.itemValue.Clone(), value.count);
        }
    }
}
