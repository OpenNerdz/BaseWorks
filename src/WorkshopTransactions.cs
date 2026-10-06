using System;
using System.Collections.Generic;

namespace NearbyCraft
{
    internal sealed partial class StorageNetworkSession
    {
        internal IEnumerable<Vector3i> StoragePositions
        {
            get { foreach (var source in sources) yield return source.Position; }
        }

        internal long CountProduct(int type, bool craftingOnly = false)
        {
            long count = 0;
            foreach (var source in sources)
            {
                if (!IsSourceValid(source)) continue;
                for (int i = 0; i < source.Storage.ItemGrid.items.Length; i++)
                {
                    var stack = source.Storage.ItemGrid.items[i];
                    if (!IsSlotLocked(source, i) && stack != null && !stack.IsEmpty() && stack.itemValue.type == type
                        && (!craftingOnly || !stack.itemValue.HasModSlots || !stack.itemValue.HasMods()))
                        count += stack.count;
                }
            }
            return count;
        }

        private bool ValidStation(TileEntityWorkstation station)
        {
            return IsAvailable && !AutomationBusy && station != null && !station.IsRemoving
                && !station.IsUserAccessing() && station.IsPlayerPlaced
                && WorkshopManager.Accessible(station) && WorkshopMachines.Supported(station)
                && world.GetTileEntity(station.ToWorldPos()) == station;
        }

        internal int CollectOutput(TileEntityWorkstation station, Action<int, int> collected = null)
        {
            if (!ValidStation(station) || station.Output == null) return 0;
            bool hasOutput = false;
            foreach (var stack in station.Output) if (stack != null && !stack.IsEmpty()) { hasOutput = true; break; }
            if (!hasOutput) return 0;
            var live = station.Output;
            var before = ItemStack.Clone(live);
            var after = ItemStack.Clone(live);
            var transaction = BeginTransaction();
            int moved = WorkshopPlanner.Collect(transaction.Plan, after);
            if (moved == 0 || !Commit(transaction, () => ValidStation(station)
                && ReferenceEquals(live, station.Output) && SameSlots(live, before), () =>
                {
                    for (int i = 0; i < live.Length; i++) live[i] = after[i];
                })) return 0;
            NotifyStation(station);
            if (collected != null)
                for (int i = 0; i < before.Length; i++)
                {
                    if (before[i] == null || before[i].IsEmpty()) continue;
                    int count = before[i].count - after[i].count;
                    if (count > 0) collected(before[i].itemValue.type, count);
                }
            return moved;
        }

        private sealed class WorkshopQueuePlan
        {
            internal TileEntityWorkstation Station;
            internal RecipeQueueItem Batch, Original;
            internal RecipeQueueItem[] Queue;
            internal int Slot;
            internal Transaction Payment;
            internal ItemStack[] InputLive, InputBefore, InputAfter;
            internal ItemStack[] FuelLive, FuelBefore, FuelAfter;
            internal ItemStack[] OutputLive, OutputBefore;
            internal ItemStack Product;
            internal bool NeedsFuel, AutomaticFuel;
            internal WorkshopReservationBank Reservations;
            internal WorkshopReservationClaim Claim;
        }

        private bool TryPlanQueueBatch(TileEntityWorkstation station, RecipeQueueItem batch,
            IList<ItemStack> reservedOutputs, bool automaticFuel, out WorkshopQueuePlan plan,
            out ItemStack missing, out string message, WorkshopReservationBank reservations = null,
            WorkshopReservationClaim claim = null)
        {
            plan = null; missing = null; message = "Station is busy";
            if (batch == null || batch.Recipe == null || batch.Multiplier <= 0
                || !ValidStation(station) || station.Queue == null || station.Queue.Length == 0
                || station.hasRecipeInQueue()) return false;
            var queue = station.Queue; int slot = queue.Length - 1; var original = queue[slot];
            if (original != null && (original.Recipe != null || original.RepairItem != null || original.Multiplier != 0)) return false;
            // Most forge probes happen before enough raw input has smelted.
            // Reject those using real unit counts before cloning every chest,
            // input and fuel array for an otherwise impossible transaction.
            if (batch.Recipe.materialBasedRecipe)
                foreach (var ingredient in batch.Recipe.ingredients)
                    if (WorkshopMachines.MaterialUnits(station, ingredient, false) < (long)ingredient.count * batch.Multiplier)
                    { missing = ingredient; message = "Needs " + ingredient.itemValue.ItemClassOrMissing.GetLocalizedItemName(); return false; }
            var payment = BeginTransaction();
            var capacity = BeginTransaction();
            var inputLive = station.Input;
            var inputBefore = ItemStack.Clone(inputLive);
            var inputAfter = ItemStack.Clone(inputLive);
            bool materials = batch.Recipe.materialBasedRecipe;
            if (materials && !WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Material_Input))
            { message = "Recipe requires a smelting workstation"; return false; }
            if (!(materials ? WorkshopPlanner.ConsumeMaterials(inputAfter, station.InputSlotCount, batch.Recipe.ingredients, batch.Multiplier, out missing)
                : WorkshopPlanner.Consume(payment.Plan, batch.Recipe.ingredients, batch.Multiplier, reservations, claim, out missing)))
            {
                message = missing == null ? "Invalid batch" : "Needs " + missing.itemValue.ItemClassOrMissing.GetLocalizedItemName();
                return false;
            }
            if (!materials && !WorkshopPlanner.Consume(capacity.Plan, batch.Recipe.ingredients, batch.Multiplier, reservations, claim, out missing)) return false;

            var fuelLive = station.Fuel;
            var fuelBefore = ItemStack.Clone(fuelLive);
            var fuelAfter = ItemStack.Clone(fuelLive);
            bool needsFuel = WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Fuel);
            if (needsFuel)
            {
                if (station.IsBesideWater) { message = "Station is blocked by water"; return false; }
                if (automaticFuel)
                {
                    StageFuel(payment.Plan, station, fuelAfter, batch.OneItemCraftTime * batch.Multiplier, reservations);
                    StageFuel(capacity.Plan, station, ItemStack.Clone(fuelLive), batch.OneItemCraftTime * batch.Multiplier, reservations);
                }
                if ((!automaticFuel && !station.IsBurning) || FuelSeconds(fuelAfter, station.BurnTimeLeft) <= 0)
                { message = automaticFuel ? "Needs wood in connected storage" : "Fuel and light the station (AUTO FUEL is off)"; return false; }
            }

            // Reserve space in a THROWAWAY plan. Prospective products must never
            // be committed before vanilla actually finishes crafting them.
            message = "Waiting for storage space";
            // Preserve the exact native tier for fixed-quality workstation tools.
            var product = new ItemStack(new ItemValue(batch.Recipe.itemValueType, batch.Quality, batch.Quality),
                checked(batch.Recipe.count * batch.Multiplier));
            if (!WorkshopPlanner.Reserve(capacity.Plan, reservedOutputs, product)) return false;
            var outputCapacity = new StorageTransferPlan();
            var outputLive = station.Output;
            var outputBefore = ItemStack.Clone(outputLive);
            outputCapacity.Add(outputLive, new bool[outputLive.Length]);
            if (outputCapacity.Deposit(product) != product.count) { message = "Waiting for workstation output space"; return false; }

            plan = new WorkshopQueuePlan
            {
                Station = station, Batch = batch, Original = original, Queue = queue, Slot = slot, Payment = payment,
                InputLive = inputLive, InputBefore = inputBefore, InputAfter = inputAfter,
                FuelLive = fuelLive, FuelBefore = fuelBefore, FuelAfter = fuelAfter,
                OutputLive = outputLive, OutputBefore = outputBefore, Product = product,
                NeedsFuel = needsFuel, AutomaticFuel = automaticFuel, Reservations = reservations, Claim = claim
            };
            return true;
        }

        private bool CommitQueueBatch(WorkshopQueuePlan plan, out string message)
        {
            message = "Storage or workstation changed; retrying";
            ulong tick = GameTimer.Instance.ticks;
            var station = plan.Station;
            if (!Commit(plan.Payment, () => ValidStation(station) && ReferenceEquals(plan.Queue, station.Queue)
                && ReferenceEquals(plan.Original, plan.Queue[plan.Slot]) && !station.hasRecipeInQueue()
                && ReferenceEquals(plan.InputLive, station.Input) && SameSlots(plan.InputLive, plan.InputBefore)
                && ReferenceEquals(plan.OutputLive, station.Output) && SameSlots(plan.OutputLive, plan.OutputBefore)
                && ReferenceEquals(plan.FuelLive, station.Fuel) && SameSlots(plan.FuelLive, plan.FuelBefore), () =>
                {
                    CopySlots(plan.InputAfter, plan.InputLive);
                    CopySlots(plan.FuelAfter, plan.FuelLive);
                    plan.Queue[plan.Slot] = plan.Batch;
                    station.lastTickTime = tick;
                })) return false;
            if (plan.NeedsFuel && plan.AutomaticFuel && !station.IsBurning) station.IsBurning = true;
            if (plan.Reservations != null && plan.Claim != null && !plan.Batch.Recipe.materialBasedRecipe)
                foreach (var ingredient in plan.Batch.Recipe.ingredients)
                    plan.Reservations.Commit(plan.Claim, ingredient.itemValue.type,
                        checked((long)ingredient.count * plan.Batch.Multiplier));
            NotifyStation(station);
            message = "Queued " + plan.Product.count;
            return true;
        }

        internal bool QueueBatch(TileEntityWorkstation station, RecipeQueueItem batch,
            IList<ItemStack> reservedOutputs, bool automaticFuel, out ItemStack missing, out string message)
        {
            WorkshopQueuePlan plan;
            return TryPlanQueueBatch(station, batch, reservedOutputs, automaticFuel, out plan, out missing, out message)
                && CommitQueueBatch(plan, out message);
        }

        // Find the largest exact transaction with logarithmic dry-runs, then
        // commit once. This keeps large orders cheap and preserves all snapshot
        // validation, output reservations and all-or-nothing ingredient payment.
        internal int QueueLargestBatch(TileEntityWorkstation station, RecipeQueueItem maximum,
            IList<ItemStack> reservedOutputs, bool automaticFuel, out ItemStack missing, out string message)
        {
            return QueueLargestBatch(station, maximum, reservedOutputs, automaticFuel, null, null, out missing, out message);
        }

        internal int QueueLargestBatch(TileEntityWorkstation station, RecipeQueueItem maximum,
            IList<ItemStack> reservedOutputs, bool automaticFuel, WorkshopReservationBank reservations,
            WorkshopReservationClaim claim, out ItemStack missing, out string message)
        {
            missing = null; message = "Invalid batch";
            if (maximum == null || maximum.Recipe == null || maximum.Multiplier <= 0) return 0;
            WorkshopQueuePlan plan;
            if (TryPlanQueueBatch(station, maximum, reservedOutputs, automaticFuel, out plan, out missing, out message, reservations, claim))
                return CommitQueueBatch(plan, out message) ? maximum.Multiplier : 0;
            int low = 0, high = maximum.Multiplier - 1;
            WorkshopQueuePlan best = null;
            while (low < high)
            {
                int amount = low + (high - low + 1) / 2;
                var candidate = ResizeBatch(maximum, amount);
                WorkshopQueuePlan attempt;
                if (TryPlanQueueBatch(station, candidate, reservedOutputs, automaticFuel, out attempt, out missing, out message, reservations, claim))
                { low = amount; best = attempt; }
                else high = amount - 1;
            }
            if (best == null) return 0;
            return CommitQueueBatch(best, out message) ? low : 0;
        }

        private static RecipeQueueItem ResizeBatch(RecipeQueueItem source, int amount)
        {
            return new RecipeQueueItem
            {
                Recipe = source.Recipe, Multiplier = (short)amount, OneItemCraftTime = source.OneItemCraftTime,
                CraftingTimeLeft = source.CraftingTimeLeft, IsCrafting = source.IsCrafting, Quality = source.Quality,
                StartingEntityId = source.StartingEntityId
            };
        }

        internal bool CanReserveProducts(IList<ItemStack> reserved, ItemStack product)
        {
            return WorkshopPlanner.Reserve(BeginTransaction().Plan, reserved, product);
        }

        private static bool SameSlots(ItemStack[] left, ItemStack[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (!StorageTransferPlan.ExactEquals(left[i], right[i])) return false;
            return true;
        }

        private static void NotifyStation(TileEntityWorkstation station)
        {
            try { station.SetModified(); }
            catch (Exception e) { Log.Error("[NearbyCraft] Workshop transfer committed but notification failed: {0}", e); }
            StorageIndex.Invalidate();
            StorageTerminalManager.RequestItemsRefresh();
        }
    }
}
