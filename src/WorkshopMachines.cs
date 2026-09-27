using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    internal static class WorkshopMachines
    {
        internal static bool Uses(TileEntityWorkstation station, TileEntityWorkstation.Module module)
        {
            return station.isModuleUsed != null && station.isModuleUsed.Length > (int)module
                && station.isModuleUsed[(int)module];
        }

        internal static bool Supported(TileEntityWorkstation station)
        {
            // Use the native workstation contract. This includes cement mixers,
            // forges and compatible mod workstations without guessing block names.
            return station != null && Uses(station, TileEntityWorkstation.Module.Output)
                && station.Queue != null && station.Output != null && station.Input != null && station.Fuel != null;
        }

        internal static bool HasSmeltingInput(TileEntityWorkstation station)
        {
            return Uses(station, TileEntityWorkstation.Module.Material_Input) && !XUiM_Recipes.DisableSmelter
                && station.Input.Take(station.InputSlotCount).Any(s => s != null && !s.IsEmpty());
        }

        internal static bool NeedsInputBalance(TileEntityWorkstation station)
        {
            int slots = Math.Min(station.InputSlotCount, station.Input.Length);
            for (int a = 0; a < slots; a++)
            {
                var source = station.Input[a];
                if (source == null || source.IsEmpty() || source.count < 2) continue;
                for (int b = 0; b < slots; b++)
                {
                    var destination = station.Input[b];
                    if (destination == null || destination.IsEmpty()
                        || (a != b && source.count > destination.count + 1 && StorageTransferPlan.Matches(source, destination))) return true;
                }
            }
            return false;
        }

        internal static long OutputRoom(TileEntityWorkstation station, Recipe recipe)
        {
            var product = new ItemStack(new ItemValue(recipe.itemValueType, recipe.craftingTier, recipe.craftingTier), 1);
            int maximum = Math.Max(1, product.itemValue.ItemClassOrMissing.MaxCount);
            long room = 0;
            foreach (var stack in station.Output)
                if (stack == null || stack.IsEmpty()) room += maximum;
                else if (StorageTransferPlan.Matches(stack, product)) room += Math.Max(0, maximum - stack.count);
            return room;
        }

        internal static float WorkSeconds(TileEntityWorkstation station)
        {
            double seconds = 0;
            foreach (var q in station.Queue)
                if (q != null && q.Recipe != null && q.Multiplier > 0)
                    seconds += Math.Max(0, q.CraftingTimeLeft) + Math.Max(0, q.Multiplier - 1) * Math.Max(0, q.OneItemCraftTime);
            if (HasSmeltingInput(station)) seconds = Math.Max(seconds, 120);
            return (float)Math.Min(120, seconds);
        }

        internal static string RawMaterial(string unit)
        {
            switch (unit)
            {
                case "unit_iron": return "resourceScrapIron";
                case "unit_brass": return "resourceScrapBrass";
                case "unit_lead": return "resourceScrapLead";
                case "unit_glass": return "resourceCrushedSand";
                case "unit_stone": return "resourceRockSmall";
                case "unit_clay": return "resourceClayLump";
                default: return null;
            }
        }

        internal static long MaterialUnits(TileEntityWorkstation station, ItemStack ingredient, bool includePending)
        {
            return MaterialUnits(station.Input, station.InputSlotCount, ingredient, includePending);
        }

        internal static long MaterialUnits(ItemStack[] input, int slots, ItemStack ingredient, bool includePending)
        {
            long units = input.Skip(slots)
                .Where(s => s != null && s.itemValue.type == ingredient.itemValue.type).Sum(s => (long)s.count);
            if (!includePending) return units;
            string name = RawMaterial(ingredient.itemValue.ItemClass.GetItemName());
            var raw = name == null ? null : ItemClass.GetItem(name, false);
            if (raw == null || raw.IsEmpty()) return units;
            string category = raw.ItemClass.MadeOfMaterial.ForgeCategory;
            return units + input.Take(slots)
                .Where(s => s != null && !s.IsEmpty() && string.Equals(s.itemValue.ItemClass.MadeOfMaterial.ForgeCategory,
                    category, StringComparison.OrdinalIgnoreCase)).Sum(s => (long)s.count * s.itemValue.ItemClass.GetWeight());
        }

        internal static int MaterialBatches(TileEntityWorkstation station, Recipe recipe, bool pending)
        {
            long batches = WorkshopRules.MaximumNativeBatch;
            foreach (var ingredient in recipe.ingredients)
                if (ingredient.count > 0) batches = Math.Min(batches, MaterialUnits(station, ingredient, pending) / ingredient.count);
            return (int)Math.Max(0, batches);
        }

        internal static double MaterialFit(TileEntityWorkstation station, Recipe recipe)
        {
            // Compare proportions, not absolute weights: 100 iron units should
            // not conceal that this recipe has no clay at all.
            if (recipe.ingredients.Count == 0) return 0;
            return recipe.ingredients.Where(i => i.count > 0)
                .Sum(i => Math.Min(1d, MaterialUnits(station, i, true) / (double)i.count));
        }

        internal static string Describe(TileEntityWorkstation station)
        {
            var active = station.Queue.LastOrDefault(q => q != null && q.Recipe != null && q.Multiplier > 0);
            string state = station.IsUserAccessing() ? "IN USE" : active == null ? "IDLE"
                : "CRAFTING " + Localization.Get(active.Recipe.GetName()) + " x" + ((long)active.Multiplier * active.Recipe.count);
            if (HasSmeltingInput(station)) state += " / " + station.Input.Take(station.InputSlotCount).Count(s => s != null && !s.IsEmpty())
                + "/" + station.InputSlotCount + " SMELT INPUTS";
            if (Uses(station, TileEntityWorkstation.Module.Fuel))
                state += " / " + (station.IsBesideWater ? "WATER BLOCKED" : (station.IsBurning ? "LIT " : "OFF ")
                    + Math.Ceiling(station.BurnTotalTimeLeft) + "s fuel");
            long output = station.Output.Where(s => s != null && !s.IsEmpty()).Sum(s => (long)s.count);
            if (output > 0) state += " / " + output + " output waiting";
            return state;
        }
    }

    internal sealed partial class StorageNetworkSession
    {
        internal bool MaintainFuel(TileEntityWorkstation station, bool automatic, out string message)
        {
            message = "";
            if (!ValidStation(station)) return false;
            if (!WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Fuel)) return true;
            if (station.IsBesideWater) { message = "Station is blocked by water"; return false; }
            float seconds = WorkshopMachines.WorkSeconds(station);
            if (!automatic) return station.IsBurning && station.BurnTotalTimeLeft > 0;
            if (seconds <= 0)
            {
                // Managed stations stop burning once both crafting and smelting finish.
                if (station.IsBurning) { station.IsBurning = false; NotifyStation(station); }
                return true;
            }
            if (station.IsBurning && station.BurnTotalTimeLeft >= seconds) return true;
            var before = ItemStack.Clone(station.Fuel);
            var after = ItemStack.Clone(before);
            var transaction = BeginTransaction();
            StageFuel(transaction.Plan, station, after, seconds);
            var live = station.Fuel;
            if (!Commit(transaction, () => ValidStation(station) && ReferenceEquals(live, station.Fuel)
                && SameSlots(live, before), () => CopySlots(after, live))) return false;
            if (station.BurnTotalTimeLeft > 0 && !station.IsBurning)
            {
                station.ResetTickTime();
                station.IsBurning = true;
            }
            NotifyStation(station);
            if (station.BurnTotalTimeLeft > 0) return true;
            message = "Needs wood in connected storage (or fuel in the station)";
            return false;
        }

        private static void StageFuel(StorageTransferPlan plan, TileEntityWorkstation station, ItemStack[] after, float seconds,
            WorkshopReservationBank reservations = null)
        {
            var wood = ItemClass.GetItem("resourceWood", false);
            if (wood == null || wood.IsEmpty()) return;
            int count = WorkshopRules.FuelCount(seconds, station.BurnTotalTimeLeft, ItemClass.GetFuelValue(wood), 100);
            if (count > 0) WorkshopPlanner.Supply(plan, after, after.Length, new ItemStack(wood, 1), count,
                reservations == null ? 0 : reservations.Reserved(wood.type));
        }

        private static float FuelSeconds(ItemStack[] fuel, float burning)
        {
            return burning + fuel.Where(s => s != null && !s.IsEmpty()).Sum(s => (float)ItemClass.GetFuelValue(s.itemValue) * s.count);
        }

        private sealed class ForgeFeedPlan
        {
            internal TileEntityWorkstation Station;
            internal Transaction Payment;
            internal ItemStack[] InputLive, InputBefore, InputAfter;
            internal ItemStack[] FuelLive, FuelBefore, FuelAfter;
            internal bool AutomaticFuel;
            internal WorkshopReservationBank Reservations;
            internal WorkshopReservationClaim Claim;
            internal readonly List<ItemStack> Supplies = new List<ItemStack>();
        }

        private bool TryPlanForgeFeed(TileEntityWorkstation station, Recipe recipe, int batches,
            bool automaticFuel, out ForgeFeedPlan feed, out string message,
            WorkshopReservationBank reservations = null, WorkshopReservationClaim claim = null)
        {
            feed = null; message = "Waiting for smelted materials";
            if (!ValidStation(station) || !WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Material_Input)
                || XUiM_Recipes.DisableSmelter || station.IsBesideWater) return false;
            var live = station.Input;
            var before = ItemStack.Clone(live);
            var transaction = BeginTransaction();
            WorkshopForgePlan plan;
            if (!WorkshopForgePlan.Create(station, recipe, batches, out plan, out message)
                || SameSlots(plan.Inputs, before)) return false;
            foreach (var supply in plan.Supplies)
                if (transaction.Plan.WithdrawPreserving(supply, supply.count,
                    reservations == null ? 0 : reservations.Keep(claim, supply.itemValue.type, supply.count)) != supply.count)
                { message = "Needs " + supply.count + " " + Localization.Get(supply.itemValue.ItemClass.GetItemName()); return false; }
            var after = plan.Inputs;
            var fuelLive = station.Fuel;
            var fuelBefore = ItemStack.Clone(fuelLive);
            var fuelAfter = ItemStack.Clone(fuelLive);
            if (automaticFuel) StageFuel(transaction.Plan, station, fuelAfter, 120, reservations);
            if (WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Fuel)
                && ((!automaticFuel && !station.IsBurning) || FuelSeconds(fuelAfter, station.BurnTimeLeft) <= 0))
            { message = automaticFuel ? "Needs wood before feeding the forge" : "Fuel and light the forge (AUTO FUEL is off)"; return false; }
            feed = new ForgeFeedPlan
            {
                Station = station, Payment = transaction, InputLive = live, InputBefore = before, InputAfter = after,
                FuelLive = fuelLive, FuelBefore = fuelBefore, FuelAfter = fuelAfter, AutomaticFuel = automaticFuel,
                Reservations = reservations, Claim = claim
            };
            feed.Supplies.AddRange(plan.Supplies.Select(s => s.Clone()));
            return true;
        }

        private bool CommitForgeFeed(ForgeFeedPlan feed, out string message)
        {
            var station = feed.Station;
            message = "Storage or forge changed; retrying";
            if (!Commit(feed.Payment, () => ValidStation(station) && ReferenceEquals(feed.InputLive, station.Input)
                && SameSlots(feed.InputLive, feed.InputBefore) && ReferenceEquals(feed.FuelLive, station.Fuel)
                && SameSlots(feed.FuelLive, feed.FuelBefore), () =>
                { CopySlots(feed.InputAfter, feed.InputLive); CopySlots(feed.FuelAfter, feed.FuelLive); })) return false;
            if (feed.AutomaticFuel && !station.IsBurning) { station.ResetTickTime(); station.IsBurning = true; }
            if (feed.Reservations != null && feed.Claim != null)
                foreach (var supply in feed.Supplies)
                    feed.Reservations.Commit(feed.Claim, supply.itemValue.type, supply.count);
            NotifyStation(station);
            message = "Smelting supplied materials";
            return true;
        }

        internal bool FeedForge(TileEntityWorkstation station, Recipe recipe, int batches, bool automaticFuel, out string message)
        {
            ForgeFeedPlan feed;
            return TryPlanForgeFeed(station, recipe, batches, automaticFuel, out feed, out message)
                && CommitForgeFeed(feed, out message);
        }

        internal int FeedLargestForge(TileEntityWorkstation station, Recipe recipe, int maximum,
            bool automaticFuel, out string message)
        {
            return FeedLargestForge(station, recipe, maximum, automaticFuel, null, null, out message);
        }

        internal int FeedLargestForge(TileEntityWorkstation station, Recipe recipe, int maximum,
            bool automaticFuel, WorkshopReservationBank reservations, WorkshopReservationClaim claim, out string message)
        {
            message = "Waiting for smelted materials";
            if (maximum <= 0) return 0;
            int covered = Math.Min(maximum, WorkshopMachines.MaterialBatches(station, recipe, true));
            if (covered >= maximum)
            {
                // Existing raw stock may cover the job while sitting in one
                // lane. Balance it even when no withdrawal is required.
                if (WorkshopMachines.NeedsInputBalance(station)) FeedForge(station, recipe, maximum, automaticFuel, out message);
                return maximum;
            }
            ForgeFeedPlan feed;
            if (TryPlanForgeFeed(station, recipe, maximum, automaticFuel, out feed, out message, reservations, claim))
                return CommitForgeFeed(feed, out message) ? maximum : 0;
            int low = covered, high = maximum - 1;
            ForgeFeedPlan best = null;
            while (low < high)
            {
                int amount = low + (high - low + 1) / 2;
                if (amount <= covered) { low = amount; continue; }
                ForgeFeedPlan attempt;
                if (TryPlanForgeFeed(station, recipe, amount, automaticFuel, out attempt, out message, reservations, claim))
                { low = amount; best = attempt; }
                else high = amount - 1;
            }
            if (low <= covered) return covered;
            return best != null && CommitForgeFeed(best, out message) ? low : 0;
        }

        private static void CopySlots(ItemStack[] from, ItemStack[] to)
        {
            for (int i = 0; i < to.Length; i++) to[i] = from[i];
        }
    }
}
