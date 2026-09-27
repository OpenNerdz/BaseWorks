// Never compiled into a release. Requires GameplayQA=true, an explicit launch
// flag, no existing world, and the dedicated NearbyCraft/qa-userdata root.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Platform;
using UnityEngine;

namespace NearbyCraft
{
    internal static class NearbyCraftGameplayQa
    {
        private static bool started;
        internal static void Update(ref ModEvents.SUnityUpdateData data)
        {
            if (started || Time.unscaledTime < 20 || !Environment.GetCommandLineArgs().Contains("-NearbyCraftGameplayQA")) return;
            started = true;
            var host = new GameObject("NearbyCraft_DisposableQA");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<NearbyCraftGameplayQaRunner>();
        }
    }

    public sealed class NearbyCraftGameplayQaRunner : MonoBehaviour
    {
        private const string Save = "NC_QA_UI_20260922_B";
        private int checks;
        private bool isolated;
        private void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); checks++; Log.Out("[NearbyCraft GameplayQA] PASS " + message); }
        private IEnumerator Start()
        {
            var run = Run(); bool failed = false;
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { failed = true; Log.Error("[NearbyCraft GameplayQA] FAILED " + e); break; }
                if (!more) break;
                yield return run.Current;
            }
            if (!failed) Log.Out("[NearbyCraft GameplayQA] COMPLETE " + checks + " assertions in disposable world.");
            if (!isolated) { Destroy(gameObject); yield break; }
            if (GameManager.Instance.World != null && GamePrefs.GetString(EnumGamePrefs.GameName) == Save)
            {
                GameManager.Instance.Disconnect();
                float deadline = Time.unscaledTime + 45;
                while (GameManager.Instance.World != null && Time.unscaledTime < deadline) yield return null;
            }
            Application.Quit(failed ? 1 : 0);
        }
        private static ItemStack Stack(string name, int count) { return new ItemStack(ItemClass.GetItem(name), count); }
        private static int Count(TEFeatureStorage storage, string name)
        { int id = ItemClass.GetItem(name).type; return storage.items.Where(s => s != null && !s.IsEmpty() && s.itemValue.type == id).Sum(s => s.count); }
        private static bool SameStacks(ItemStack[] left, ItemStack[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (!StorageTransferPlan.ExactEquals(left[i], right[i])) return false;
            return true;
        }
        private void Click(XUiController group, string name)
        { var button = group.GetChildById(name); Check(button != null, "UI control " + name); button.Pressed(-1); }
        private void RequestLabels(XUiController group, string phase)
        {
            foreach (string name in new[] { "workshopOnce", "workshopStock", "workshopAdd", "workshopAmount", "workshopQty10", "workshopQty100", "workshopQty1000", "workshopQtyMax" })
                foreach (var label in group.GetChildById(name).ViewComponent.uiTransform.GetComponentsInChildren<UILabel>(true))
                {
                    Log.Out("[NearbyCraft GameplayQA] Caption " + phase + " / " + name + " text=" + label.text
                        + " active=" + label.gameObject.activeInHierarchy + " visible=" + label.isVisible
                        + " alpha=" + label.CalculateFinalAlpha(Time.frameCount) + " vertices=" + label.geometry.hasVertices
                        + " draw=" + (label.drawCall == null ? "null" : label.drawCall.isActiveAndEnabled.ToString())
                        + " depth=" + label.depth + " font=" + label.ambigiousFont + " shader=" + label.shader);
                    Check(label.gameObject.activeInHierarchy && label.isVisible
                        && label.CalculateFinalAlpha(Time.frameCount) > .9f && label.geometry.hasVertices
                        && label.drawCall != null && label.drawCall.isActiveAndEnabled,
                        phase + " caption geometry: " + name);
                }
        }
        private void JobLabels(XUiController group, string phase)
        {
            foreach (string name in new[] { "workshopCatalogLabel", "workshopName0", "workshopPhase0", "workshopGoal0", "workshopDetail0" })
            {
                var control = group.GetChildById(name);
                Check(control != null, phase + " job text control: " + name);
                var label = control.ViewComponent.uiTransform.GetComponentsInChildren<UILabel>(true).FirstOrDefault();
                Check(label != null && label.gameObject.activeInHierarchy && label.isVisible
                    && label.CalculateFinalAlpha(Time.frameCount) > .9f && label.geometry.hasVertices
                    && label.drawCall != null && label.drawCall.isActiveAndEnabled,
                    phase + " readable job text geometry: " + name);
            }
        }
        private IEnumerator Run()
        {
            string root = GameIO.GetSaveGameRootDir().Replace('\\', '/');
            Check(root.IndexOf("/NearbyCraft/qa-userdata/Saves", StringComparison.OrdinalIgnoreCase) >= 0, "isolated save root");
            Check(GameManager.Instance.World == null, "no user world loaded");
            isolated = true;
            string state = Path.Combine(root, "NearbyCraftState");
            Directory.CreateDirectory(state);
            WorkshopStore.Initialize(state);
            GamePrefs.Set(EnumGamePrefs.GameWorld, "Navezgane");
            GamePrefs.Set(EnumGamePrefs.GameWorldLocationType, (int)PathAbstractions.EAbstractedLocationType.GameData);
            GamePrefs.Set(EnumGamePrefs.GameSaveStorageType, (int)UserDataStorageType.DeviceLocal);
            GamePrefs.Set(EnumGamePrefs.UserWorldStorageType, (int)UserDataStorageType.DeviceLocal);
            GamePrefs.Set(EnumGamePrefs.GameName, Save);
            GamePrefs.Set(EnumGamePrefs.GameMode, "GameModeSurvival");
            GamePrefs.Set(EnumGamePrefs.ServerEnabled, false);
            GamePrefs.Set(EnumGamePrefs.ServerMaxPlayerCount, 1);
            GamePrefs.Set(EnumGamePrefs.EnemySpawnMode, false);
            GamePrefs.Set(EnumGamePrefs.SkipSpawnButton, true);
            XUiC_NewContinueBase.LastStartedNewGame = true;
            Check(SingletonMonoBehaviour<ConnectionManager>.Instance.StartServers("", true) == NetworkConnectionError.NoError, "solo QA world starts");
            float deadline = Time.unscaledTime + 240;
            while ((GameManager.Instance.World == null || GameManager.Instance.World.GetPrimaryPlayer() == null || GameManager.Instance.IsStartingGame)
                && Time.unscaledTime < deadline) yield return null;
            var world = GameManager.Instance.World;
            var player = world == null ? null : world.GetPrimaryPlayer();
            Check(player != null && NearbyCraftMod.CanUseLocalStorage, "solo player and Harmony ready");
            yield return new WaitForSecondsRealtime(8);
            player.PlayerUI.windowManager.CloseAllOpenModalWindows();
            Check(GamePrefs.GetString(EnumGamePrefs.GameName) == Save && GameIO.GetSaveGameDir().Replace('\\', '/').Contains("/NearbyCraft/qa-userdata/"), "fixture save guard");
            GameStats.Set(EnumGameStats.ChunkStabilityEnabled, false);
            var p = new Vector3i(player.position); p.x += 3; p.y = (int)world.GetHeightAt(p.x, p.z) + 1;
            for (int x = -1; x <= 12; x++)
            for (int z = -1; z <= 12; z++)
            {
                var at = p + new Vector3i(x, 0, z);
                if (world.IsWithinTraderArea(at)) throw new Exception("QA footprint intersects protected trader");
                world.SetBlockRPC(at + Vector3i.down, Block.GetBlockValue("terrStone"));
                for (int y = 0; y < 4; y++) world.SetBlockRPC(at + new Vector3i(0, y, 0), BlockValue.Air);
            }
            world.SetBlockRPC(p, Block.GetBlockValue(StorageTerminalManager.BlockName));
            var console = (TileEntityComposite)world.GetTileEntity(p); console.SetOwner(PlatformManager.InternalLocalUserIdentifier);
            var boxPos = p + new Vector3i(2, 0, 0);
            world.SetBlockRPC(boxPos, Block.GetBlockValue("cntWoodWritableCrate"));
            var box = (TileEntityComposite)world.GetTileEntity(boxPos); box.SetOwner(PlatformManager.InternalLocalUserIdentifier);
            var storage = box.GetFeature<TEFeatureStorage>(); storage.items = ItemStack.CreateArray(storage.items.Length);
            string[] names = { "workbench", "cementMixer", "forge", "campfire", "chemistryStation", "cntDewCollector", "cntApiary", "cntChickenCoop" };
            var positions = new[] { new Vector3i(4,0,0), new Vector3i(8,0,0), new Vector3i(0,0,4), new Vector3i(4,0,4),
                new Vector3i(8,0,4), new Vector3i(0,0,8), new Vector3i(4,0,8), new Vector3i(8,0,8) };
            for (int i = 0; i < names.Length; i++)
            {
                world.SetBlockRPC(p + positions[i], Block.GetBlockValue(names[i]));
                var station = world.GetTileEntity(p + positions[i]) as TileEntityWorkstation;
                if (station != null) station.IsPlayerPlaced = true;
            }
            foreach (var offset in new[] { new Vector3i(4,0,11), new Vector3i(8,0,11) })
            {
                world.SetBlockRPC(p + offset, Block.GetBlockValue("forge"));
                ((TileEntityWorkstation)world.GetTileEntity(p + offset)).IsPlayerPlaced = true;
            }
            yield return new WaitForSecondsRealtime(2);
            var devices = WorkshopManager.FindDevices(world, p);
            foreach (string name in names) Check(devices.Any(d => d.block.GetBlockName() == name), "discovers " + name);
            var workstations = devices.OfType<TileEntityWorkstation>().ToList();
            Check(workstations.Count(s => s.block.GetBlockName() == "forge") == 3, "three real forges available for work sharing");
            storage.items[0] = Stack("resourceRockSmall", 1000);
            storage.items[1] = Stack("resourceWood", 200);
            storage.items[2] = Stack("drinkJarEmpty", 10);
            var dew = devices.OfType<TileEntityCollector>().First(d => d.block.GetBlockName() == "cntDewCollector");
            dew.Items[0] = Stack("drinkJarRiverWater", 2);
            var ui = player.PlayerUI.xui;
            var inventory = ui.PlayerInventory;
            ItemStack[] originalBackpack = ItemStack.Clone(inventory.GetBackpackItemStacks());
            ItemStack[] originalStorage = ItemStack.Clone(storage.items);
            try
            {
                var testBackpack = ItemStack.CreateArray(originalBackpack.Length);
                testBackpack[0] = Stack("resourceMechanicalParts", 5);
                inventory.SetBackpackItemStacks(testBackpack);
                storage.items = ItemStack.CreateArray(originalStorage.Length);
                storage.items[0] = Stack("resourceMechanicalParts", 10);
                box.SetModified();
                StorageIndex.ForceFreshForCraft();
                var duplicated = new[] { Stack("resourceMechanicalParts", 8), Stack("resourceMechanicalParts", 8) };
                Check(!StorageIndex.HasItems(inventory, duplicated, 1),
                    "nearby crafting aggregates duplicate requirements instead of double-counting supplies");
                var exact = new[] { Stack("resourceMechanicalParts", 15) };
                Check(StorageIndex.HasItems(inventory, exact, 1), "nearby crafting sees one exact player-plus-storage payment");
                var removed = new System.Collections.Generic.List<ItemStack>();
                StorageIndex.RemoveItems(inventory, exact, 1, removed);
                Check(inventory.GetBackpackItemStacks().All(s => s.IsEmpty()) && Count(storage, "resourceMechanicalParts") == 0,
                    "nearby crafting commits player and storage ingredients together");
                Check(removed.Sum(s => s.count) == 15, "nearby crafting reports the exact committed ingredients");

                var mixerRecipe = XUiM_Recipes.GetRecipes().First(r => r.GetName() == "cementMixer");
                inventory.SetBackpackItemStacks(ItemStack.CreateArray(originalBackpack.Length));
                storage.items = ItemStack.CreateArray(originalStorage.Length);
                int mixerParts = 0;
                for (int i = 0; i < mixerRecipe.ingredients.Count; i++)
                {
                    ItemStack ingredient = mixerRecipe.ingredients[i].Clone();
                    ingredient.itemValue.Meta = 70 + i;
                    ingredient.itemValue.Seed = (ushort)(900 + i);
                    ingredient.itemValue.Flags ^= ItemValue.cFlagsActivated;
                    storage.items[i] = ingredient;
                    mixerParts += ingredient.count;
                }
                box.SetModified();
                StorageIndex.ForceFreshForCraft();
                Check(CraftingBridge.HasItems(inventory, mixerRecipe.ingredients, 1),
                    "cement mixer Craft-button bridge accepts nearby ingredients using native item matching");
                removed.Clear();
                StorageIndex.RemoveItems(inventory, mixerRecipe.ingredients, 1, removed);
                Check(storage.items.All(s => s == null || s.IsEmpty()) && removed.Sum(s => s.count) == mixerParts,
                    "cement mixer consumes exactly its nearby recipe components");

                testBackpack = ItemStack.CreateArray(originalBackpack.Length);
                testBackpack[0] = Stack("resourceMechanicalParts", 5);
                inventory.SetBackpackItemStacks(testBackpack);
                storage.items = ItemStack.CreateArray(originalStorage.Length);
                box.SetModified();
                var transactionSession = new StorageNetworkSession(world, player, p, NearbyCraftMod.Config);
                Check(transactionSession.Rescan() && !transactionSession.Rescan(),
                    "unchanged terminal inventories skip repeat catalog rebuilds");
                storage.items[0] = Stack("resourceMechanicalParts", 1);
                Check(transactionSession.Rescan() && transactionSession.TotalItemCount == 1,
                    "a new chest stack refreshes the terminal catalog");
                storage.items[0].count++;
                Check(transactionSession.Rescan() && transactionSession.TotalItemCount == 2,
                    "in-place stack count changes refresh the terminal catalog");
                storage.items[0].count--;
                Check(transactionSession.Rescan() && transactionSession.TotalItemCount == 1,
                    "restoring an in-place stack count refreshes the catalog again");
                storage.items[0] = ItemStack.Empty.Clone();
                Check(transactionSession.Rescan() && transactionSession.TotalItemCount == 0,
                    "emptying a chest stack refreshes the catalog");
                Check(transactionSession.DepositBackpack(inventory, false) == 5
                    && inventory.GetBackpackItemStacks().All(s => s.IsEmpty())
                    && Count(storage, "resourceMechanicalParts") == 5,
                    "bulk deposit commits the staged backpack and storage changes together");

                storage.items = ItemStack.CreateArray(originalStorage.Length);
                storage.items[0] = Stack("resourceMechanicalParts", 10);
                box.SetModified();
                Check(transactionSession.Rescan() && transactionSession.TotalItemCount == 10,
                    "external chest replacement refreshes the terminal catalog");
                Check(!transactionSession.Rescan(), "unchanged replacement is not redrawn repeatedly");
                PackedBoolArray locks = storage.SlotLocks;
                if (NearbyCraftMod.Config.RespectLockedSlots && storage.HasSlotLocksSupport
                    && locks != null && locks.Length > 0)
                {
                    bool originalLock = locks[0];
                    try
                    {
                        locks[0] = true;
                        Check(transactionSession.Rescan() && transactionSession.TotalItemCount == 0,
                            "locking a chest slot removes its stack from the terminal catalog");
                    }
                    finally { locks[0] = originalLock; }
                    Check(transactionSession.Rescan() && transactionSession.TotalItemCount == 10,
                        "unlocking a chest slot restores its stack to the terminal catalog");
                }
                bool applied = false, rolledBack = false;
                LoadoutSwapResult swap;
                bool exchanged = transactionSession.TryExchangeLoadout(
                    new[] { ItemStack.Empty.Clone() }, new[] { Stack("resourceMechanicalParts", 1) },
                    () => true,
                    () => { applied = true; storage.items[0].count--; },
                    () => rolledBack = true, out swap);
                Check(!exchanged && applied && rolledBack, "loadout swap rolls the player side back when storage changes during apply");
                Check(Count(storage, "resourceMechanicalParts") == 9,
                    "rejected loadout swap does not commit its planned storage withdrawal");
                Check(TerminalRules.CanShiftToInventory(true, false) && TerminalRules.CanShiftToInventory(false, true)
                    && !TerminalRules.CanShiftToInventory(false, false),
                    "terminal shift-click accepts either valid player destination");
            }
            finally
            {
                inventory.SetBackpackItemStacks(originalBackpack);
                storage.items = originalStorage;
                box.SetModified();
                StorageIndex.Invalidate();
            }
            var timingForge = workstations.First(s => s.block.GetBlockName() == "forge");
            var cementRecipe = XUiM_Recipes.GetRecipes().First(r => r.GetName() == "resourceCement");
            var originalTools = ItemStack.Clone(timingForge.Tools);
            var cachedTools = EffectManager.slotsCached;
            int cachedFrame = EffectManager.slotsQueriedFrame, cachedEntity = EffectManager.slotsQueriedForEntity;
            try
            {
                float plain = WorkshopManager.PrepareRecipe(cementRecipe, ui, timingForge).craftingTime;
                timingForge.Tools[0] = new ItemStack(new ItemValue(ItemClass.GetItem("toolAnvil").type, 6, 6), 1);
                float upgraded = WorkshopManager.PrepareRecipe(cementRecipe, ui, timingForge).craftingTime;
                Check(upgraded < plain && upgraded > 0, "native anvil quality bonus changes this machine's actual queued recipe time");
                Check(ReferenceEquals(cachedTools, EffectManager.slotsCached) && cachedFrame == EffectManager.slotsQueriedFrame
                    && cachedEntity == EffectManager.slotsQueriedForEntity, "machine-specific effect evaluation restores vanilla tool cache");
            }
            finally { for (int i = 0; i < originalTools.Length; i++) timingForge.Tools[i] = originalTools[i]; }
            Check(console.block.OnBlockActivated("Search", world, p, world.GetBlock(p), player), "console activation opens storage");
            yield return new WaitForSecondsRealtime(.5f);
            Check(StorageTerminalManager.IsOpen, "real storage window is open");
            Check(StorageTerminalManager.ActiveSession.IsAvailable, "storage session passes access and distance checks");
            Check(ui.DragAndDropWindow.CurrentStack.IsEmpty(), "cursor is empty before navigating");
            Check(ui.FindWindowGroupByName(WorkshopManager.WindowGroupId) is XUiC_WorkshopWindowGroup, "production window controller resolves");
            var storageGroup = ui.FindWindowGroupByName(StorageTerminalManager.WindowGroupId);
            Check(storageGroup.GetChildById("terminalSections") != null, "storage and production have a dedicated section switch");
            string productionSummary = "";
            storageGroup.GetBindingValueInternal(ref productionSummary, "terminal_production_summary");
            Check(productionSummary == "PLAN JOBS & MACHINES", "new networks explain the production entry");
            ScreenCapture.CaptureScreenshot(Path.Combine(root, "..", "production-ui-final-storage.png"));
            yield return new WaitForSecondsRealtime(1);
            Click(ui.FindWindowGroupByName(StorageTerminalManager.WindowGroupId), "nearbyCraftTerminalProduction");
            yield return null;
            var group = ui.FindWindowGroupByName(WorkshopManager.WindowGroupId);
            Check(player.PlayerUI.windowManager.IsWindowOpen(WorkshopManager.WindowGroupId), "production opens from storage");
            Check(WorkshopStore.Get(p) != null && !WorkshopStore.Get(p).Enabled && WorkshopStore.Get(p).Console == p, "single console self-links and starts paused");
            Click(group, "workshopStorage");
            yield return null;
            Check(StorageTerminalManager.IsOpen, "Storage returns from Production to the same console");
            storageGroup.GetBindingValueInternal(ref productionSummary, "terminal_production_summary");
            Check(productionSummary == "AUTOMATION PAUSED", "section switch reports the live controller state");
            Click(storageGroup, "nearbyCraftTerminalProduction");
            yield return null;
            Check(player.PlayerUI.windowManager.IsWindowOpen(WorkshopManager.WindowGroupId), "section switch reopens Production");
            var productionMixerRecipe = XUiM_Recipes.GetRecipes().First(r => r.GetName() == "cementMixer");
            var productionAnvilRecipe = XUiM_Recipes.FilterRecipesByWorkstation("forge", XUiM_Recipes.GetRecipes())
                .First(r => r.GetName() == "toolAnvil");
            var variableQualityRecipe = XUiM_Recipes.GetRecipes().First(r => r.GetName() == "gunHandgunT1Pistol");
            Check(WorkshopManager.Productive(productionMixerRecipe), "non-stackable Cement Mixer is eligible for production");
            Check(WorkshopManager.Productive(productionAnvilRecipe), "fixed-tier Anvil is eligible for production");
            Check(!WorkshopManager.Supported(variableQualityRecipe), "variable-quality equipment stays in native player crafting");
            string catalogMode = "";
            group.GetBindingValueInternal(ref catalogMode, "workshop_catalog_mode");
            Check(catalogMode == "READY", "recipe catalog defaults to the compact ready-machine view");
            Click(group, "workshopCatalogMode");
            group.GetBindingValueInternal(ref catalogMode, "workshop_catalog_mode");
            Check(catalogMode == "ALL", "recipe catalog can expose every supported recipe");
            Click(group, "workshopCatalogMode");
            ((XUiC_TextInput)group.GetChildById("workshopSearch")).Text = "cementMixer";
            yield return null;
            string catalogResult = "";
            group.GetBindingValueInternal(ref catalogResult, "workshop_result_name0");
            Check(catalogResult == Localization.Get("cementMixer"), "search finds Cement Mixer even outside the compact ready list");
            ((XUiC_TextInput)group.GetChildById("workshopSearch")).Text = "toolAnvil";
            yield return null;
            group.GetBindingValueInternal(ref catalogResult, "workshop_result_name0");
            Check(catalogResult == Localization.Get("toolAnvil"), "search finds fixed-tier Anvil production");
            ((XUiC_TextInput)group.GetChildById("workshopSearch")).Text = "resourceConcreteMix";
            Click(group, "workshopResult0");
            string requestRequirements = "";
            group.GetBindingValueInternal(ref requestRequirements, "workshop_recipe");
            Check(requestRequirements.Contains("\n") && requestRequirements.StartsWith(Localization.Get("cementMixer")),
                "request card separates the workstation from its material requirements");

            var forgeSand = XUiM_Recipes.FilterRecipesByWorkstation("forge", XUiM_Recipes.GetRecipes())
                .First(r => r.GetName() == "resourceCrushedSand" && r.materialBasedRecipe);
            var mixerSand = XUiM_Recipes.FilterRecipesByWorkstation("cementMixer", XUiM_Recipes.GetRecipes())
                .First(r => r.GetName() == "resourceCrushedSand" && !r.materialBasedRecipe);
            Check(!WorkshopManager.Productive(forgeSand) && WorkshopManager.Productive(mixerSand),
                "native forge recovery Sand recipe is rejected while the productive mixer recipe remains available");

            var planningNetwork = new StorageNetworkSession(world, player, p, NearbyCraftMod.Config, p, true);
            planningNetwork.Rescan(false);
            var planningBank = new WorkshopReservationBank();
            var planningController = WorkshopStore.Get(p);
            var productionPlanner = new WorkshopProductionPlanner(planningNetwork, player, ui, planningController,
                workstations, planningController.AutoCraft);
            int rockType = ItemClass.GetItem("resourceRockSmall").type;
            int concreteType = ItemClass.GetItem("resourceConcreteMix").type;
            var thousandPlan = productionPlanner.Plan(concreteType, 1000, productionPlanner.CreatePool(), planningBank);
            Check(thousandPlan.Maximum > 0 && thousandPlan.Maximum < 1000 && !string.IsNullOrEmpty(thousandPlan.Missing),
                "whole-job planner reports a partial maximum and missing base component for 1,000 Concrete Mix");
            Check(planningBank.Reserved(rockType) > 0 && planningBank.Reserved(rockType) <= 1000,
                "whole-job planner reserves the available Stone across Sand, Cement and final Concrete steps");
            ((XUiC_TextInput)group.GetChildById("workshopAmount")).Text = "1000";
            yield return null;
            string planningHint = "";
            group.GetBindingValueInternal(ref planningHint, "workshop_mode_hint");
            Check(planningHint.Contains("MAX NOW " + thousandPlan.Maximum) && planningHint.Contains("MISSING"),
                "production screen shows the planner maximum and missing component before the request is submitted");
            Click(group, "workshopQtyMax");
            Check(((XUiC_TextInput)group.GetChildById("workshopAmount")).Text == thousandPlan.Maximum.ToString(),
                "MAX selects the complete plan's currently craftable quantity");

            foreach (int n in new[] { 1, 10, 100, 1000 })
            {
                Click(group, "workshopQty" + n);
                Check(((XUiC_TextInput)group.GetChildById("workshopAmount")).Text == n.ToString(), "quantity shortcut " + n);
            }
            Click(group, "workshopStock");
            string requestLabel = "";
            group.GetBindingValueInternal(ref requestLabel, "workshop_add");
            Check(requestLabel.StartsWith("KEEP"), "Keep stocked changes request mode");
            Click(group, "workshopOnce");
            ((XUiC_TextInput)group.GetChildById("workshopAmount")).Text = "24";
            Click(group, "workshopAdd");
            Check(WorkshopStore.Get(p).Targets.Single().Once && WorkshopStore.Get(p).Targets[0].Remaining == 24
                && WorkshopStore.Get(p).Enabled, "one Craft click requests concrete and starts automation");
            deadline = Time.unscaledTime + 10;
            while (WorkshopStore.Get(p).Smelting.Count < 2 && Time.unscaledTime < deadline) yield return null;
            Check(WorkshopStore.Get(p).Smelting.Count >= 2, "native production spreads cement preparation across multiple forges");
            Check(workstations.Where(s => s.block.GetBlockName() == "forge").Any(s => s.Input.Take(s.InputSlotCount).Count(i => !i.IsEmpty()) > 1),
                "native forge receives split raw material in parallel input lanes");
            var rollingJob = WorkshopStore.Get(p).Smelting.First(j => j.Item == "resourceCement" && j.Batches > 1);
            var rollingForge = workstations.First(s => s.ToWorldPos() == rollingJob.Position);
            var rollingRecipe = WorkshopManager.PrepareRecipe(cementRecipe, ui, rollingForge);
            int rollingBefore = rollingJob.Count;
            for (int step = 0; step < 2000 && WorkshopMachines.MaterialBatches(rollingForge, rollingRecipe, false) == 0; step++)
                rollingForge.HandleMaterialInput(.25f);
            Check(WorkshopMachines.MaterialBatches(rollingForge, rollingRecipe, false) > 0
                && WorkshopMachines.MaterialBatches(rollingForge, rollingRecipe, false) < rollingJob.Batches,
                "native forge has a partial Cement chunk ready before its full allocation");
            var rollingNetwork = new StorageNetworkSession(world, player, p, NearbyCraftMod.Config, p, true);
            rollingNetwork.Rescan(false);
            new WorkshopScheduler(rollingNetwork, player, ui, WorkshopStore.Get(p), workstations).Run();
            var rollingRemainder = WorkshopStore.Get(p).Smelting.FirstOrDefault(j => j.Position == rollingForge.ToWorldPos());
            Check(rollingForge.hasRecipeInQueue() && rollingRemainder != null && rollingRemainder.Count > 0
                && rollingRemainder.Count < rollingBefore,
                "native forge starts a ready rolling Cement chunk and retains the exact remainder");
            int assigned = WorkshopStore.Get(p).Smelting.Sum(j => j.Count);
            WorkshopStore.Initialize(state);
            Check(WorkshopStore.Get(p).Smelting.Sum(j => j.Count) == assigned, "live smelting assignments survive settings reload");
            yield return new WaitForSecondsRealtime(1);
            Check(!group.GetChildById("workshopRow1").ViewComponent.IsVisible, "unused job rows are hidden");
            RequestLabels(group, "jobs");
            JobLabels(group, "jobs");
            ScreenCapture.CaptureScreenshot(Path.Combine(root, "..", "production-ui-final-orders.png"));
            yield return new WaitForSecondsRealtime(1);
            Click(group, "workshopMachines");
            yield return new WaitForSecondsRealtime(.5f);
            string machinePage = "";
            group.GetBindingValueInternal(ref machinePage, "workshop_page");
            Check(machinePage.StartsWith("MACHINES"), "MACHINES tab changes controller state");
            Click(group, "workshopPageNext");
            yield return new WaitForSecondsRealtime(1);
            var pageLabel = group.GetChildById("workshopPageLabel").ViewComponent as XUiV_Label;
            Check(pageLabel != null && pageLabel.Text == "MACHINES 2 / 2", "rendered machine page binding refreshes");
            Check(group.GetChildById("productionSearch").ViewComponent.IsVisible
                && group.GetChildById("machineOverview").ViewComponent.IsVisible, "rendered tab visibility refreshes");
            RequestLabels(group, "machines");
            ScreenCapture.CaptureScreenshot(Path.Combine(root, "..", "production-ui-final-machines.png"));
            yield return new WaitForSecondsRealtime(1);
            Click(group, "workshopSettings");
            yield return new WaitForSecondsRealtime(.5f);
            Check(group.GetChildById("workshopOptions").ViewComponent.IsVisible
                && !group.GetChildById("workshopRows").ViewComponent.IsVisible, "Options hides job controls without hiding recipe picker");
            Click(group, "workshopFuel"); Check(!WorkshopStore.Get(p).AutoFuel, "Options fuel toggle works");
            Click(group, "workshopFuel"); Check(WorkshopStore.Get(p).AutoFuel, "Options fuel toggle restores automatic supply");
            yield return new WaitForSecondsRealtime(1);
            RequestLabels(group, "options");
            ScreenCapture.CaptureScreenshot(Path.Combine(root, "..", "production-ui-final-options.png"));
            yield return new WaitForSecondsRealtime(1);
            for (int capture = 0; capture < 3; capture++)
            {
                yield return new WaitForSecondsRealtime(.37f);
                ScreenCapture.CaptureScreenshot(Path.Combine(root, "..", "production-ui-final-options-" + capture + ".png"));
            }
            yield return new WaitForSecondsRealtime(1);
            player.PlayerUI.windowManager.Close(WorkshopManager.WindowGroupId);
            bool sawForgeInput = false, sawForgeQueue = false, sawMixerQueue = false, sawAccurateMixerStatus = false;
            deadline = Time.unscaledTime + 90;
            while (Count(storage, "resourceConcreteMix") < 24 && Time.unscaledTime < deadline)
            {
                foreach (var station in workstations)
                {
                    if (station.block.GetBlockName() == "forge")
                    { sawForgeInput |= WorkshopMachines.HasSmeltingInput(station); sawForgeQueue |= station.hasRecipeInQueue(); }
                    if (station.block.GetBlockName() == "cementMixer" && station.hasRecipeInQueue())
                    {
                        sawMixerQueue = true;
                        string active;
                        sawAccurateMixerStatus |= WorkshopManager.TargetStatus.TryGetValue(
                            WorkshopManager.TargetKey(p, "resourceConcreteMix"), out active)
                            && active.StartsWith("Crafting ") && !active.StartsWith("Waiting ");
                    }
                    // Speed only the disposable test's native smelting/recipe timers.
                    station.HandleMaterialInput(120f);
                    station.HandleRecipeQueue(120f);
                }
                yield return new WaitForSecondsRealtime(.3f);
            }
            if (Count(storage, "resourceConcreteMix") != 24)
            {
                Log.Out("[NearbyCraft GameplayQA] Manager states: " + string.Join("; ", WorkshopManager.Status.Values));
                Log.Out("[NearbyCraft GameplayQA] Order states: " + string.Join("; ", WorkshopManager.TargetStatus.Values));
            }
            Check(Count(storage, "resourceConcreteMix") == 24, "stone -> parallel smelted cement + sand -> concrete returns exactly twenty-four items");
            Check(sawForgeInput && sawForgeQueue && sawMixerQueue, "real forge input and native forge/mixer queues were used");
            Check(sawAccurateMixerStatus, "busy mixer status reports active crafting instead of a false free-machine wait");
            Check(Count(storage, "resourceRockSmall") < 1000 && Count(storage, "resourceWood") < 200, "actual stone and fuel were consumed");
            Check(WorkshopStore.Get(p).Targets[0].Remaining == 0, "one-time order records committed progress");
            Check(WorkshopStore.Get(p).Completed.Count == 1 && WorkshopStore.Get(p).Completed[0].Produced == 24
                && WorkshopStore.Get(p).Completed[0].VerifiedDelivery, "completed history records exactly the collected output");
            player.PlayerUI.windowManager.Open(WorkshopManager.WindowGroupId, true);
            yield return new WaitForSecondsRealtime(1);
            Check(!group.GetChildById("workshopRow0").ViewComponent.IsVisible, "completed request leaves the active Jobs view");
            Click(group, "workshopHistory");
            yield return new WaitForSecondsRealtime(1);
            string historyPage = ""; group.GetBindingValueInternal(ref historyPage, "workshop_page");
            Check(historyPage.StartsWith("HISTORY") && group.GetChildById("workshopRow0").ViewComponent.IsVisible,
                "Completed tab displays persisted finished request");
            ScreenCapture.CaptureScreenshot(Path.Combine(root, "..", "production-ui-final-completed.png"));
            yield return new WaitForSecondsRealtime(1);
            Click(group, "workshopToggle0");
            Check(((XUiC_TextInput)group.GetChildById("workshopAmount")).Text == "24"
                && WorkshopStore.Get(p).Targets[0].Remaining == 0, "Repeat fills quantity without silently placing another order");
            player.PlayerUI.windowManager.Close(WorkshopManager.WindowGroupId);
            Check(Count(storage, "drinkJarRiverWater") >= 2 && Count(storage, "drinkJarEmpty") < 10, "collector exports water and takes jars");
            var concrete = storage.items.First(s => !s.IsEmpty() && s.itemValue.type == ItemClass.GetItem("resourceConcreteMix").type);
            concrete.count -= 3;
            yield return new WaitForSecondsRealtime(5);
            Check(Count(storage, "resourceConcreteMix") == 21 && !workstations.Any(s => s.hasRecipeInQueue()), "completed MAKE ONCE does not replenish withdrawn items");
            string error;
            player.PlayerUI.windowManager.Open(WorkshopManager.WindowGroupId, true);
            yield return new WaitForSecondsRealtime(.5f);
            ((XUiC_TextInput)group.GetChildById("workshopSearch")).Text = "resourceConcreteMix";
            Click(group, "workshopResult0"); Click(group, "workshopStock");
            ((XUiC_TextInput)group.GetChildById("workshopAmount")).Text = "24";
            Click(group, "workshopAdd");
            Check(!WorkshopStore.Get(p).Targets[0].Once && WorkshopStore.Get(p).Targets[0].CompletedUtcTicks == 0,
                "requesting stock mode revives the product without deleting its completed history");
            player.PlayerUI.windowManager.Close(WorkshopManager.WindowGroupId);
            deadline = Time.unscaledTime + 45;
            while (Count(storage, "resourceConcreteMix") < 24 && Time.unscaledTime < deadline)
            {
                foreach (var station in workstations) { station.HandleMaterialInput(120f); station.HandleRecipeQueue(120f); }
                yield return new WaitForSecondsRealtime(.3f);
            }
            Check(Count(storage, "resourceConcreteMix") == 24, "KEEP STOCK replenishes withdrawn items");
            Check(WorkshopStore.Edit(p, c =>
            {
                c.Targets.Clear(); c.Smelting.Clear(); c.Enabled = true;
                c.Targets.Add(new WorkshopTarget { Item = "resourceCement", Once = true, Target = 24, Remaining = 24, TrackDelivery = true });
            }, out error), "prepare dedicated save/reload production order");
            deadline = Time.unscaledTime + 10;
            while (WorkshopStore.Get(p).Smelting.Count < 2 && Time.unscaledTime < deadline) yield return null;
            Check(WorkshopStore.Get(p).Smelting.Count >= 2, "pending native smelting exists before world save");
            WorkshopStore.Edit(p, c => c.Enabled = false, out error);
            int savedAssignments = WorkshopStore.Get(p).Smelting.Sum(j => j.Count);
            int initialCement = Count(storage, "resourceCement");
            GameManager.Instance.Disconnect();
            deadline = Time.unscaledTime + 90;
            while (GameManager.Instance.World != null && Time.unscaledTime < deadline) yield return null;
            Check(GameManager.Instance.World == null, "disposable world saved and unloaded");
            yield return new WaitForSecondsRealtime(3);
            WorkshopStore.Initialize(state);
            Check(WorkshopStore.Get(p).Smelting.Sum(j => j.Count) == savedAssignments, "saved assignments reload before loading native world");
            XUiC_NewContinueBase.LastStartedNewGame = false;
            Check(SingletonMonoBehaviour<ConnectionManager>.Instance.StartServers("", true) == NetworkConnectionError.NoError, "saved disposable world starts again");
            deadline = Time.unscaledTime + 240;
            while ((GameManager.Instance.World == null || GameManager.Instance.World.GetPrimaryPlayer() == null || GameManager.Instance.IsStartingGame)
                && Time.unscaledTime < deadline) yield return null;
            world = GameManager.Instance.World; player = world == null ? null : world.GetPrimaryPlayer();
            Check(player != null && GamePrefs.GetString(EnumGamePrefs.GameName) == Save, "saved QA world reloads with its player");
            GameStats.Set(EnumGameStats.ChunkStabilityEnabled, false);
            yield return new WaitForSecondsRealtime(4);
            player.PlayerUI.windowManager.CloseAllOpenModalWindows();
            storage = ((TileEntityComposite)world.GetTileEntity(boxPos)).GetFeature<TEFeatureStorage>();
            workstations = WorkshopManager.FindStations(world, p);
            Check(workstations.Count(s => s.block.GetBlockName() == "forge") == 3 && Count(storage, "resourceConcreteMix") == 24,
                "native machines and completed inventory survive world reload");
            Check(WorkshopStore.Get(p).Completed.Any(c => c.Item == "resourceConcreteMix" && c.Produced == 24 && c.VerifiedDelivery),
                "completed history survives native world and settings reload");
            Check(WorkshopStore.Get(p).Smelting.Count >= 2 && workstations.Any(WorkshopMachines.HasSmeltingInput), "native smelting inputs and their assignments survive world reload");
            WorkshopStore.Edit(p, c => c.Enabled = true, out error);
            deadline = Time.unscaledTime + 90;
            while (Count(storage, "resourceCement") < initialCement + 24 && Time.unscaledTime < deadline)
            {
                foreach (var station in workstations) { station.HandleMaterialInput(120f); station.HandleRecipeQueue(120f); }
                yield return new WaitForSecondsRealtime(.3f);
            }
            Check(Count(storage, "resourceCement") == initialCement + 24 && WorkshopStore.Get(p).Targets[0].Remaining == 0
                && WorkshopStore.Get(p).Smelting.Count == 0, "resumed world finishes exact requested output without duplicating assigned work");
            WorkshopStore.Edit(p, c => c.Enabled = false, out error);
            world.SetBlockRPC(p, Block.GetBlockValue("nearbyCraftStorageTerminalTier2"));
            yield return new WaitForSecondsRealtime(3);
            Check(WorkshopStore.Get(p) != null && WorkshopStore.Get(p).Targets.Count == 1, "native console tier upgrade preserves orders");
            WorkshopStore.Initialize(state);
            Check(WorkshopStore.Get(p).Targets[0].Target == 24 && !WorkshopStore.Get(p).Enabled, "settings reload retains orders and pause state");
            Check(WorkshopStore.Edit(p, c =>
            {
                c.Targets.Clear(); c.Smelting.Clear(); c.Enabled = true;
                c.Targets.Add(new WorkshopTarget { Item = "resourceCrushedSand", Once = true, Target = 120,
                    Remaining = 120, TrackDelivery = true });
            }, out error), "prepare adaptive native mixer order");
            var mixer = workstations.First(s => s.block.GetBlockName() == "cementMixer");
            deadline = Time.unscaledTime + 10;
            while (!mixer.hasRecipeInQueue() && Time.unscaledTime < deadline) yield return null;
            var adaptive = mixer.Queue.LastOrDefault(q => q != null && q.Recipe != null && q.Multiplier > 0);
            Check(adaptive != null && adaptive.Recipe.GetName() == "resourceCrushedSand" && adaptive.Multiplier == 120,
                "real mixer queues all 120 available cycles in one capacity-aware native batch");
            var reloadedUi = player.PlayerUI.xui;
            var adaptiveGroup = reloadedUi.FindWindowGroupByName(WorkshopManager.WindowGroupId) as XUiC_WorkshopWindowGroup;
            if (adaptiveGroup != null) adaptiveGroup.SetPosition(p);
            player.PlayerUI.windowManager.Open(WorkshopManager.WindowGroupId, true);
            yield return new WaitForSecondsRealtime(1);
            Check(adaptiveGroup != null
                && player.PlayerUI.windowManager.IsWindowOpen(WorkshopManager.WindowGroupId)
                && adaptiveGroup.GetChildById("workshopRow0").ViewComponent.IsVisible,
                "adaptive production queue remains visible in the native-style screen");
            JobLabels(adaptiveGroup, "adaptive");
            ScreenCapture.CaptureScreenshot(Path.Combine(root, "..", "production-ui-final-adaptive.png"));
            yield return new WaitForSecondsRealtime(1);
            string etaLabel = "";
            adaptiveGroup.GetBindingValueInternal(ref etaLabel, "workshop_eta0");
            Check(etaLabel.StartsWith("ETA: ~"), "active native crafting exposes a numeric job completion estimate");
            // Stay in one frame so automatic collection cannot race this
            // simulated native UI take-all operation in the disposable world.
            mixer.HandleRecipeQueue(100000f);
            Check(WorkshopManager.CountOutput(mixer, ItemClass.GetItem("resourceCrushedSand").type) >= 120,
                "native mixer finishes the adaptive batch before manual collection");
            new XUiM_Workstation(mixer).SetOutputStacks(ItemStack.CreateArray(mixer.Output.Length));
            Check(WorkshopStore.Get(p).Targets[0].Returned == 120 && WorkshopStore.Get(p).Targets[0].CollectedManually == 120,
                "Harmony output-model hook records take-all before an automation tick");
            var manualNetwork = new StorageNetworkSession(world, player, p, NearbyCraftMod.Config, p, true);
            manualNetwork.Rescan(false);
            new WorkshopScheduler(manualNetwork, player, reloadedUi, WorkshopStore.Get(p), workstations).Run();
            Check(WorkshopStore.Get(p).Targets[0].CompletedUtcTicks > 0,
                "manually collected native output completes without a permanent wait");
        }
    }
}
