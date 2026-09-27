using System;
using System.Collections.Generic;

namespace NearbyCraft
{
    // A claim is a logical reservation, not a hidden inventory. Transactions
    // still validate the real chest snapshots immediately before committing.
    // Claims only stop another automated step from spending components which a
    // completed whole-job plan assigned elsewhere.
    internal sealed class WorkshopReservationClaim
    {
        private readonly Dictionary<int, long> remaining = new Dictionary<int, long>();
        internal readonly string RecipeKey;

        internal WorkshopReservationClaim(string recipeKey) { RecipeKey = recipeKey; }
        internal IEnumerable<KeyValuePair<int, long>> Items { get { return remaining; } }
        internal long Remaining(int type) { long value; return remaining.TryGetValue(type, out value) ? value : 0; }
        internal void Add(int type, long count)
        {
            if (count <= 0) return;
            remaining[type] = checked(Remaining(type) + count);
        }
        internal long Take(int type, long count)
        {
            long used = Math.Min(Math.Max(0, count), Remaining(type));
            if (used <= 0) return 0;
            long left = remaining[type] - used;
            if (left == 0) remaining.Remove(type); else remaining[type] = left;
            return used;
        }
    }

    internal sealed class WorkshopReservationBank
    {
        private readonly Dictionary<int, long> totals = new Dictionary<int, long>();

        internal void Add(WorkshopReservationClaim claim)
        {
            if (claim == null) return;
            foreach (var item in claim.Items)
            {
                long current;
                totals.TryGetValue(item.Key, out current);
                totals[item.Key] = checked(current + item.Value);
            }
        }

        internal long Reserved(int type)
        {
            long value;
            return totals.TryGetValue(type, out value) ? value : 0;
        }

        internal long Keep(WorkshopReservationClaim claim, int type, long proposed)
        {
            long release = claim == null ? 0 : Math.Min(claim.Remaining(type), Math.Max(0, proposed));
            return Math.Max(0, Reserved(type) - release);
        }

        internal void Commit(WorkshopReservationClaim claim, int type, long consumed)
        {
            if (claim == null || consumed <= 0) return;
            long released = claim.Take(type, consumed);
            if (released <= 0) return;
            long left = Reserved(type) - released;
            if (left <= 0) totals.Remove(type); else totals[type] = left;
        }
    }
}
