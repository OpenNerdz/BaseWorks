using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Scripting;

namespace NearbyCraft
{
    [Preserve]
    public sealed class XUiC_WorkshopWindowGroup : XUiController
    {
        private Vector3i position, accessPosition;
        private World world;
        private bool open, machines, settings, history, once = true, showAllRecipes;
        private float nextRefresh, nextEstimate, noticeUntil;
        private string notice = "";
        private string removeConfirm;
        private XUiC_TextInput search, amount;
        private int selected, page, resultPage;
        private StorageNetworkSession network;
        private WorkshopProductionPlan preview;
        private string previewItem;
        private string catalogStations = "";
        private int previewEntered, previewNeeded;
        private bool previewOnce;
        private readonly Dictionary<string, long> stockCache = new Dictionary<string, long>();
        private Dictionary<string, WorkshopCompletionEstimate> estimates = new Dictionary<string, WorkshopCompletionEstimate>();
        private List<TileEntity> stations = new List<TileEntity>();
        private List<Product> allProducts = new List<Product>(), products = new List<Product>();
        private sealed class Product
        {
            internal string Name, Title, Icon, Hint, Station;
            internal bool Unlocked, Present;
        }
        internal void SetPosition(Vector3i value, Vector3i? access = null) { position = value; accessPosition = access ?? value; }

        public override void Init()
        {
            base.Init();
            search = GetChildById("workshopSearch") as XUiC_TextInput;
            amount = GetChildById("workshopAmount") as XUiC_TextInput;
            if (search != null) search.OnChangeHandler += (sender, text, fromCode) => Filter(text);
            if (amount != null) amount.OnChangeHandler += (sender, text, fromCode) => { preview = null; IsDirty = true; };
            Bind("workshopPrevious", () => { resultPage = Math.Max(0, resultPage - 1); IsDirty = true; });
            Bind("workshopNext", () => { resultPage = Math.Min(ResultPages() - 1, resultPage + 1); IsDirty = true; });
            Bind("workshopCatalogMode", () => { showAllRecipes = !showAllRecipes; Filter(search == null ? "" : search.Text); });
            Bind("workshopAdd", AddTarget);
            Bind("workshopRun", () => Change(c => c.Enabled = !c.Enabled));
            Bind("workshopLink", Link);
            Bind("workshopStorage", OpenStorage);
            Bind("workshopOrders", () => Tab(false, false));
            Bind("workshopMachines", () => Tab(true, false));
            Bind("workshopSettings", () => Tab(false, true));
            Bind("workshopHistory", () => Tab(false, false, true));
            Bind("workshopOnce", () => { once = true; preview = null; IsDirty = true; });
            Bind("workshopStock", () => { once = false; preview = null; IsDirty = true; });
            Bind("workshopFuel", () => Change(c => c.AutoFuel = !c.AutoFuel));
            Bind("workshopAutoCraft", () => Change(c => c.AutoCraft = !c.AutoCraft));
            Bind("workshopPagePrevious", () => { page = Math.Max(0, page - 1); IsDirty = true; });
            Bind("workshopPageNext", () => { page = Math.Min(PageCount() - 1, page + 1); IsDirty = true; });
            Bind("workshopClearDone", () => Change(c =>
            {
                foreach (var target in c.Targets.Where(t => t.Once && t.Remaining == 0 && Pending(t.Item) == 0
                    && (!t.TrackDelivery || t.Returned >= t.Queued))) WorkshopStore.RecordCompletion(c, target, target.TrackDelivery);
            }));
            foreach (int quantity in new[] { 1, 10, 100, 1000 })
            {
                int n = quantity; Bind("workshopQty" + n, () => { if (amount != null) amount.Text = n.ToString(); });
            }
            Bind("workshopQtyMax", SetMaximum);
            for (int i = 0; i < WorkshopRules.VisibleRows; i++)
            {
                int index = i;
                Bind("workshopResult" + i, () =>
                {
                    int row = resultPage * WorkshopRules.VisibleRows + index;
                    if (row >= products.Count) return;
                    selected = row;
                    var c = WorkshopStore.Get(position);
                    var target = c == null ? null : c.Targets.Find(t => t.Item == products[row].Name);
                    once = target == null || target.Once;
                    preview = null;
                    IsDirty = true;
                });
                Bind("workshopToggle" + i, () => Toggle(index));
                Bind("workshopRemove" + i, () => Remove(index));
            }
        }

        private void Tab(bool showMachines, bool showSettings, bool showHistory = false)
        { machines = showMachines; settings = showSettings; history = showHistory; page = 0; removeConfirm = null; nextEstimate = 0; IsDirty = true; }

        public override void OnOpen()
        {
            world = GameManager.Instance.World; open = true; noticeUntil = nextEstimate = 0f;
            base.OnOpen();
            if (WorkshopStore.Get(position) == null) { Change(c => { }); Link(); noticeUntil = 0; }
            page = resultPage = 0; machines = settings = history = false; once = true; showAllRecipes = false; removeConfirm = null;
            stations = WorkshopManager.FindDevices(world, position);
            BuildCatalog();
            if (search != null) { search.Text = ""; search.FocusOnOpen = false; }
            if (amount != null) { amount.Text = "100"; amount.FocusOnOpen = false; }
            Filter("");
            RefreshNetwork();
            xui.RecenterWindowGroup(windowGroup);
            IsDirty = true;
        }

        public override void OnClose()
        { open = false; world = null; network = null; preview = null; estimates.Clear(); base.OnClose(); }

        public override void Update(float dt)
        {
            if (open && Time.realtimeSinceStartup >= nextRefresh)
            {
                nextRefresh = Time.realtimeSinceStartup + 1f;
                if (!Ready()) { xui.playerUI.windowManager.Close(WorkshopManager.WindowGroupId); return; }
                stations = WorkshopManager.FindDevices(world, position);
                string available = CatalogStationKey();
                if (available != catalogStations)
                {
                    BuildCatalog();
                    Filter(search == null ? "" : search.Text);
                }
                RefreshNetwork();
                page = Math.Min(page, PageCount() - 1);
                IsDirty = true;
            }
            // Refresh bindings; changed view properties invalidate themselves. Rewriting
            // every static label/sprite each tick also needlessly rebuilds native text.
            if (open && IsDirty) { RefreshBindingsSelfAndChildren(); IsDirty = false; }
            base.Update(dt);
        }

        private void RefreshNetwork()
        {
            stockCache.Clear(); preview = null;
            var c = WorkshopStore.Get(position);
            if (c == null || !c.Linked) { network = null; estimates.Clear(); return; }
            network = new StorageNetworkSession(world, xui.playerUI.entityPlayer, c.Console, NearbyCraftMod.Config, position, true);
            network.Rescan(false);
            if (!machines && !history && !settings && Time.realtimeSinceStartup >= nextEstimate)
            {
                nextEstimate = Time.realtimeSinceStartup + 2f;
                if (c.Targets.Any(t => t.CompletedUtcTicks == 0))
                    estimates = new WorkshopCompletionEstimator(network, xui.playerUI.entityPlayer, xui, c,
                        stations.OfType<TileEntityWorkstation>().ToList()).Estimate();
                else estimates.Clear();
            }
        }

        private void BuildCatalog()
        {
            var known = XUiM_Recipes.GetRecipes().Where(WorkshopManager.Productive).ToList();
            var available = new HashSet<Recipe>();
            var c = WorkshopStore.Get(position);
            foreach (string name in stations.OfType<TileEntityWorkstation>()
                .Where(s => c == null || !c.ExcludedStations.Contains(s.ToWorldPos().ToString()))
                .Select(s => s.block.GetBlockName()).Distinct())
                foreach (var recipe in XUiM_Recipes.FilterRecipesByWorkstation(name, XUiM_Recipes.GetRecipes())) available.Add(recipe);
            allProducts = known.GroupBy(r => r.GetName()).Select(group =>
            {
                var recipes = group.ToList();
                var unlocked = recipes.Where(r => r.IsUnlocked(xui.playerUI.entityPlayer)).ToList();
                var runnable = unlocked.Where(available.Contains).ToList();
                var shown = runnable.Count > 0 ? runnable : unlocked.Count > 0 ? unlocked : recipes;
                var recipe = shown[0];
                string station = string.Join(" / ", shown.Select(RecipeStation).Distinct().OrderBy(s => s));
                return new Product { Name = group.Key, Title = DisplayName(group.Key),
                    Icon = recipe.GetOutputItemClass().GetIconName(), Station = station,
                    Unlocked = unlocked.Count > 0, Present = runnable.Count > 0,
                    Hint = RecipeHint(shown) };
            }).OrderByDescending(p => p.Unlocked && p.Present).ThenByDescending(p => p.Unlocked).ThenBy(p => p.Title).ToList();
            catalogStations = CatalogStationKey();
        }

        private string CatalogStationKey()
        {
            var c = WorkshopStore.Get(position);
            return string.Join("|", stations.OfType<TileEntityWorkstation>()
                .Where(s => c == null || !c.ExcludedStations.Contains(s.ToWorldPos().ToString()))
                .Select(s => s.block.GetBlockName()).Distinct().OrderBy(name => name));
        }

        private static string RecipeStation(Recipe recipe)
        {
            return DisplayName(string.IsNullOrEmpty(recipe.craftingArea) ? "workbench" : recipe.craftingArea);
        }

        private static string RecipeHint(IEnumerable<Recipe> recipes)
        {
            var alternatives = recipes.Select(recipe => RecipeStation(recipe) + " / "
                    + string.Join(" + ", recipe.ingredients.Select(i => i.count + " "
                        + DisplayName(i.itemValue.ItemClass.GetItemName())))
                    + ToolHint(recipe))
                .Distinct().OrderBy(value => value).ToList();
            if (alternatives.Count == 1)
            {
                // The request card has two lines: machine first, requirements
                // second. This is easier to scan than a single long slash list.
                int separator = alternatives[0].IndexOf(" / ", StringComparison.Ordinal);
                return separator < 0 ? alternatives[0]
                    : alternatives[0].Substring(0, separator) + "\n" + alternatives[0].Substring(separator + 3);
            }
            const int visible = 3;
            string hint = "Automation can choose: " + string.Join("  OR  ", alternatives.Take(visible));
            return alternatives.Count > visible ? hint + "  (+" + (alternatives.Count - visible) + " more)" : hint;
        }

        private static string ToolHint(Recipe recipe)
        {
            if (recipe.craftingToolType == 0) return "";
            ItemClass tool = ItemClass.GetForId(recipe.craftingToolType);
            return tool == null ? " / Requires tool #" + recipe.craftingToolType
                : " / Requires " + DisplayName(tool.GetItemName());
        }

        private bool Ready()
        {
            return world != null && ReferenceEquals(world, GameManager.Instance.World) && NearbyCraftMod.CanUseLocalStorage
                && WorkshopManager.IsManager(world.GetBlock(position).Block) && WorkshopManager.IsManager(world.GetBlock(accessPosition).Block)
                && WorkshopManager.Accessible(world.GetTileEntity(position)) && WorkshopManager.Accessible(world.GetTileEntity(accessPosition))
                && (xui.playerUI.entityPlayer.position - accessPosition.ToVector3()).sqrMagnitude <= 64f
                && (position.ToVector3() - accessPosition.ToVector3()).sqrMagnitude <= NearbyCraftMod.Config.TerminalRange * NearbyCraftMod.Config.TerminalRange;
        }

        private void Bind(string name, Action action)
        {
            var button = GetChildById(name);
            if (button != null) button.OnPress += (sender, mouse) =>
            { if (Ready() && xui.DragAndDropWindow.CurrentStack.IsEmpty()) action(); };
            else Log.Error("[NearbyCraft] Missing workshop control: {0}", name);
        }

        private bool Change(Action<WorkshopControllerData> action)
        {
            string error;
            bool saved = WorkshopStore.Edit(position, action, out error);
            if (!saved) Show(error);
            if (saved) nextEstimate = 0;
            IsDirty = true;
            return saved;
        }

        private int Row(int index) { return page * WorkshopRules.VisibleRows + index; }
        private int PageCount()
        {
            var c = WorkshopStore.Get(position);
            int count = machines ? stations.Count : c == null ? 0 : history ? c.Completed.Count : c.Targets.Count(t => t.CompletedUtcTicks == 0);
            return Math.Max(1, (count + WorkshopRules.VisibleRows - 1) / WorkshopRules.VisibleRows);
        }
        private int ResultPages() { return Math.Max(1, (products.Count + WorkshopRules.VisibleRows - 1) / WorkshopRules.VisibleRows); }
        private Product SelectedProduct { get { return selected >= 0 && selected < products.Count ? products[selected] : null; } }
        private static WorkshopTarget ActiveTarget(WorkshopControllerData c, int row)
        { return c == null ? null : c.Targets.Where(t => t.CompletedUtcTicks == 0).Skip(row).FirstOrDefault(); }

        private void Remove(int index)
        {
            if (machines || settings || history) return;
            var target = ActiveTarget(WorkshopStore.Get(position), Row(index));
            if (target == null) return;
            if (removeConfirm != target.Item || Time.realtimeSinceStartup >= noticeUntil)
            { removeConfirm = target.Item; Show("Click Remove again to cancel " + DisplayName(target.Item) + ". Already queued work is kept."); return; }
            Change(c => { c.Targets.RemoveAll(t => t.Item == target.Item); c.Smelting.RemoveAll(j => j.Owner == target.Item); });
            removeConfirm = null; Show("Request removed. Existing machine contents were kept.");
        }

        private void Toggle(int index)
        {
            int row = Row(index);
            if (history)
            {
                var c = WorkshopStore.Get(position);
                if (c == null || row >= c.Completed.Count) return;
                var entry = c.Completed[row];
                if (search != null) search.Text = entry.Item;
                Filter(entry.Item); once = true;
                if (amount != null) amount.Text = entry.Requested.ToString();
                Show("Ready to repeat " + DisplayName(entry.Item) + ". Press Craft to confirm.");
                return;
            }
            if (!machines)
            {
                Change(c => { var target = ActiveTarget(c, row); if (!settings && target != null) target.Enabled = !target.Enabled; });
                return;
            }
            if (row >= stations.Count) return;
            var pos = stations[row].ToWorldPos();
            Change(c => { if (!c.ExcludedStations.Remove(pos.ToString())) { c.ExcludedStations.Add(pos.ToString()); c.Smelting.RemoveAll(j => j.Position == pos); } });
        }

        private void OpenStorage()
        {
            var c = WorkshopStore.Get(position);
            if (c == null || !c.Linked || !StorageTerminalManager.IsTerminal(world.GetBlock(c.Console).Block)
                || !WorkshopManager.Accessible(world.GetTileEntity(c.Console))
                || (c.Console.ToVector3() - position.ToVector3()).sqrMagnitude > NearbyCraftMod.Config.TerminalRange * NearbyCraftMod.Config.TerminalRange)
            { Show("Place or link a Storage Console first."); return; }
            var group = xui.FindWindowGroupByName(StorageTerminalManager.WindowGroupId) as XUiC_StorageTerminalWindowGroup;
            if (group == null) return;
            var wm = xui.playerUI.windowManager;
            group.SetTerminal(c.Console, accessPosition, position);
            wm.Close(WorkshopManager.WindowGroupId); wm.Open(StorageTerminalManager.WindowGroupId, true);
        }

        private void Link()
        {
            Vector3i terminal;
            if (StorageTerminalManager.IsTerminal(world.GetBlock(position).Block)) terminal = position;
            else if (!LoadoutLockerManager.TryFindTerminal(world, position, NearbyCraftMod.Config.TerminalRange, out terminal))
            { Show("Place an accessible Storage Console within " + NearbyCraftMod.Config.TerminalRange + " blocks."); return; }
            if (Change(c => { c.Linked = true; c.ConsoleX = terminal.x; c.ConsoleY = terminal.y; c.ConsoleZ = terminal.z; c.Enabled = false; c.Smelting.Clear(); }))
                Show("Console linked. Choose an item and click Craft to start.");
        }

        private void Filter(string text)
        {
            text = (text ?? "").Trim();
            // Keep the idle list useful and compact. Explicit searches always
            // include the full catalog so unavailable or locked recipes remain
            // discoverable without another click.
            products = allProducts.Where(p => showAllRecipes || text.Length > 0 || (p.Unlocked && p.Present))
                .Where(p => p.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                || p.Title.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(p => p.Title.Equals(text, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(text, StringComparison.OrdinalIgnoreCase))
                .ToList();
            selected = resultPage = 0;
            preview = null;
            IsDirty = true;
        }

        private WorkshopProductionPlan Preview(int entered, out int needed)
        {
            needed = 0;
            var product = SelectedProduct;
            var c = WorkshopStore.Get(position);
            if (product == null || network == null || c == null || entered < 1) return null;
            long deficit = once ? entered : Math.Max(0, entered - Stored(product.Name) - Pending(product.Name));
            needed = (int)Math.Min(WorkshopRules.MaximumTarget, deficit);
            if (preview != null && previewItem == product.Name && previewEntered == entered
                && previewOnce == once && previewNeeded == needed) return preview;
            var usable = stations.OfType<TileEntityWorkstation>()
                .Where(s => !c.ExcludedStations.Contains(s.ToWorldPos().ToString())).ToList();
            var selectedItem = ItemClass.GetItem(product.Name, false);
            var outputs = new List<int>();
            if (selectedItem != null && !selectedItem.IsEmpty()) outputs.Add(selectedItem.type);
            foreach (var target in c.Targets)
            {
                if (!target.Enabled || target.CompletedUtcTicks != 0) continue;
                var item = ItemClass.GetItem(target.Item, false);
                if (item != null && !item.IsEmpty()) outputs.Add(item.type);
            }
            var planner = new WorkshopProductionPlanner(network, xui.playerUI.entityPlayer, xui, c, usable,
                c.AutoCraft, outputs);
            var pool = planner.CreatePool(); var bank = new WorkshopReservationBank();
            // Existing jobs reserve first. Adding another MAKE request for the
            // same item is additional work; KEEP STOCKED replaces that item's
            // previous mode/target and therefore excludes it from this preview.
            foreach (var target in c.Targets)
            {
                if (!target.Enabled || target.CompletedUtcTicks != 0 || (!once && target.Item == product.Name)) continue;
                var item = ItemClass.GetItem(target.Item, false);
                if (item == null || item.IsEmpty()) continue;
                long demand = target.Once ? target.Remaining
                    : Math.Max(0, target.Target - Stored(target.Item) - Pending(target.Item));
                if (demand > 0) planner.Plan(item.type, (int)Math.Min(WorkshopRules.MaximumTarget, demand), pool, bank);
            }
            preview = selectedItem == null || selectedItem.IsEmpty()
                ? null : planner.Plan(selectedItem.type, needed, pool, bank);
            previewItem = product.Name; previewEntered = entered; previewOnce = once; previewNeeded = needed;
            return preview;
        }

        private void AddTarget()
        {
            int count;
            var product = SelectedProduct;
            if (product == null) { Show("Search for an item, then select it from the list."); return; }
            if (!product.Unlocked) { Show("Unlock " + product.Title + " before requesting it."); return; }
            if (!product.Present) { Show("Place an enabled " + product.Station + " within range first."); return; }
            if (amount == null || !int.TryParse(amount.Text, out count) || count < 1 || count > WorkshopRules.MaximumTarget)
            { Show("Enter a quantity from 1 to " + WorkshopRules.MaximumTarget + "."); return; }
            var controller = WorkshopStore.Get(position);
            var existing = controller == null ? null : controller.Targets.Find(t => t.Item == product.Name);
            if (existing != null && existing.Once && once && existing.CompletedUtcTicks == 0 && (long)existing.Target + count > WorkshopRules.MaximumTarget)
            { Show("That exceeds the maximum unfinished quantity."); return; }
            if (existing == null && controller != null && controller.Targets.Count(t => t.CompletedUtcTicks == 0) >= WorkshopRules.MaximumTargets)
            { Show("All 24 job slots are used. Clear finished jobs or remove one."); return; }
            bool pending = Pending(product.Name) > 0;
            int needed;
            var availability = Preview(count, out needed);
            if (!Change(c =>
            {
                c.Targets.RemoveAll(t => t.CompletedUtcTicks != 0 && t.Item != product.Name);
                var target = c.Targets.Find(t => t.Item == product.Name);
                if (target == null) c.Targets.Add(new WorkshopTarget { Item = product.Name, Target = count, Once = once, Remaining = once ? count : 0, TrackDelivery = once });
                else
                {
                    bool changedMode = target.Once != once;
                    bool fresh = changedMode || target.CompletedUtcTicks != 0 || (target.Once && target.Remaining == 0 && !pending && (!target.TrackDelivery || target.Returned >= target.Queued));
                    if (fresh) { target.Queued = target.Returned = target.CollectedManually = 0; target.CompletedUtcTicks = 0; target.TrackDelivery = once; }
                    if (changedMode) { c.Smelting.RemoveAll(j => j.Owner == product.Name); target.Remaining = 0; }
                    target.Target = !fresh && once ? Math.Min(WorkshopRules.MaximumTarget, target.Target + count) : count;
                    target.Once = once;
                    if (once) target.Remaining += count;
                    target.Enabled = true;
                }
                c.Enabled = true;
            })) return;
            controller = WorkshopStore.Get(position);
            machines = settings = history = false;
            page = Math.Max(0, controller.Targets.Where(t => t.CompletedUtcTicks == 0).ToList().FindIndex(t => t.Item == product.Name)) / WorkshopRules.VisibleRows;
            if (availability != null && availability.Maximum < needed)
                Show((once ? "Requested " + count + ". " : "Stock target set to " + count + ". ")
                    + "Can make " + availability.Maximum + " of " + needed + " now; waiting for supplies."
                    + (string.IsNullOrEmpty(availability.Missing) ? "" : " Missing " + availability.Missing + "."));
            else Show(once ? "Requested " + count + " " + product.Title + ". Full component plan is ready."
                : "Keeping " + count + " " + product.Title + " in storage. Automatic top-up is on.");
            preview = null;
        }

        private void SetMaximum()
        {
            if (amount == null || SelectedProduct == null) return;
            preview = null;
            int needed;
            var available = Preview(WorkshopRules.MaximumTarget, out needed);
            if (available == null || available.Maximum <= 0)
            {
                Show("Nothing can be made now" + (available == null || string.IsNullOrEmpty(available.Missing)
                    ? "." : ". Missing " + available.Missing + "."));
                return;
            }
            long value = once ? available.Maximum
                : Math.Min(WorkshopRules.MaximumTarget, Stored(SelectedProduct.Name) + Pending(SelectedProduct.Name) + available.Maximum);
            amount.Text = value.ToString();
            Show("Maximum currently plannable quantity selected: " + value + ".");
        }

        private void Show(string message) { notice = message; noticeUntil = Time.realtimeSinceStartup + 8f; IsDirty = true; }
        private static string DisplayName(string name) { string s = Localization.Get(name); return string.IsNullOrEmpty(s) ? name : s; }
        private long Pending(string name)
        {
            var item = ItemClass.GetItem(name, false);
            return item == null || item.IsEmpty() ? 0 : stations.OfType<TileEntityWorkstation>()
                .Sum(s => WorkshopManager.CountOutput(s, item.type) + WorkshopManager.CountQueued(s, item.type));
        }
        private long Stored(string name)
        {
            long count;
            if (stockCache.TryGetValue(name, out count)) return count;
            var item = ItemClass.GetItem(name, false);
            return stockCache[name] = network == null || item == null || item.IsEmpty() ? 0 : network.CountProduct(item.type);
        }
        private static string ItemIcon(string name)
        {
            var item = ItemClass.GetItem(name, false);
            return item == null || item.IsEmpty() || item.ItemClass == null ? "" : item.ItemClass.GetIconName();
        }
        private string MachinePhase(TileEntity station, WorkshopControllerData c)
        {
            if (station == null) return "";
            if (c == null || c.ExcludedStations.Contains(station.ToWorldPos().ToString())) return "DISABLED";
            string state;
            if (WorkshopManager.StationStatus.TryGetValue(station.ToWorldPos(), out state)
                && (state.StartsWith("Needs") || state.StartsWith("Fuel") || state.StartsWith("Station is blocked")
                    || state.StartsWith("Waiting for storage") || state.StartsWith("Not enough"))) return "BLOCKED";
            if (station.IsUserAccessing()) return "IN USE";
            var machine = station as TileEntityWorkstation;
            if (machine != null && (machine.hasRecipeInQueue() || WorkshopMachines.HasSmeltingInput(machine))) return "WORKING";
            return "READY";
        }
        private string Detail(WorkshopTarget target)
        {
            if (!target.Enabled) return "Paused. Already queued work can finish.";
            if (target.Once && target.TrackDelivery && target.Remaining == 0 && target.Returned < target.Queued && Pending(target.Item) == 0)
                return "Output not collected. Check removed/excluded machines or cancelled native queues.";
            string value;
            return WorkshopManager.TargetStatus.TryGetValue(WorkshopManager.TargetKey(position, target.Item), out value)
                ? value : "Finding machines and checking supplies...";
        }

        // The scheduler keeps a precise diagnostic string for tooltips and the
        // log. Rows use a shorter version so live state remains full-sized and
        // readable instead of shrinking an entire sentence into one line.
        private static string CompactDetail(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("Next batch starts when the current machine is free", "Next batch waits for a free machine")
                .Replace("Next batch starts after output collection", "Next batch waits for collection")
                .Replace(" finished awaiting collection", " ready for collection")
                .Replace("Done / returned to storage", "Returned to storage")
                .Replace("In stock / automatic top-up ready", "Target met / automatic top-up ready")
                .Replace("NATIVE PRODUCTION / ", "")
                .Replace(" output waiting", " ready")
                .Replace("Install the required bees/chickens in this device", "Needs bees or chickens");
            if (value.StartsWith("Producing / ")) value = value.Substring("Producing / ".Length);
            else if (value.StartsWith("Smelting / ")) value = value.Substring("Smelting / ".Length);
            else if (value.StartsWith("Crafting / ")) value = value.Substring("Crafting / ".Length);
            return value;
        }
        private string Phase(WorkshopTarget target, WorkshopControllerData c)
        {
            if (!target.Enabled || !c.Enabled) return "PAUSED";
            if (target.Once && target.Remaining == 0) return Pending(target.Item) > 0 ? "CRAFTING"
                : target.TrackDelivery && target.Returned < target.Queued ? "CHECK OUTPUT" : "DONE";
            if (!target.Once && Stored(target.Item) >= target.Target) return "IN STOCK";
            var jobs = c.Smelting.Where(j => j.Owner == target.Item).ToList();
            if (jobs.Count > 0)
            {
                string state;
                if (jobs.Any(j => WorkshopManager.StationStatus.TryGetValue(j.Position, out state)
                    && (state.StartsWith("Needs") || state.StartsWith("Fuel") || state.StartsWith("Waiting for storage")))) return "NEEDS SUPPLIES";
                return "PRODUCING";
            }
            if (Pending(target.Item) > 0) return "CRAFTING";
            string detail = Detail(target);
            if (detail.StartsWith("Preparing") || detail.StartsWith("Crafting")) return "PREPARING";
            if (detail.StartsWith("Waiting") || detail.StartsWith("Finding")) return "WAITING";
            return "NEEDS SUPPLIES";
        }
        private static string StateColor(string phase)
        {
            if (phase == "DONE" || phase == "IN STOCK" || phase == "READY") return "135,205,155,255";
            if (phase == "NEEDS SUPPLIES" || phase == "BLOCKED" || phase == "CHECK OUTPUT") return "235,183,99,255";
            if (phase == "PAUSED" || phase == "DISABLED") return "146,160,170,255";
            return "105,203,215,255";
        }

        public override bool GetBindingValueInternal(ref string value, string bindingName)
        {
            var c = world == null ? null : WorkshopStore.Get(position);
            var product = SelectedProduct;
            switch (bindingName)
            {
                case "workshop_orders_visible": value = (!machines && !settings && !history).ToString(); return true;
                case "workshop_archive_visible": value = (!machines && !settings && !history && c != null && c.Targets.Any(t => t.Once && t.CompletedUtcTicks == 0
                    && t.Remaining == 0 && Pending(t.Item) == 0 && (!t.TrackDelivery || t.Returned >= t.Queued))).ToString(); return true;
                case "workshop_machines_visible": value = machines.ToString(); return true;
                case "workshop_settings_visible": value = settings.ToString(); return true;
                case "workshop_list_visible": value = (!settings).ToString(); return true;
                case "workshop_link_visible": value = (world != null && !StorageTerminalManager.IsTerminal(world.GetBlock(position).Block)).ToString(); return true;
                case "workshop_empty": value = (!settings && (machines ? stations.Count == 0 : c == null || (history ? c.Completed.Count == 0 : ActiveTarget(c, 0) == null))).ToString(); return true;
                case "workshop_empty_text": value = machines ? "No machines nearby\nPlace workstations within " + NearbyCraftMod.Config.TerminalRange + " blocks."
                    : history ? "No completed requests yet\nNew jobs appear here after output reaches storage."
                    : "No active jobs\nChoose an item on the left and click Craft."; return true;
                case "workshop_once_color": value = once ? "96,96,96,255" : "64,64,64,255"; return true;
                case "workshop_stock_color": value = !once ? "96,96,96,255" : "64,64,64,255"; return true;
                case "workshop_catalog_mode": value = showAllRecipes ? "ALL" : "READY"; return true;
                case "workshop_catalog_color": value = showAllRecipes ? "96,96,96,255" : "64,64,64,255"; return true;
                case "workshop_catalog_label_color": value = showAllRecipes ? "255,255,255,255" : "170,170,170,255"; return true;
                case "workshop_orders_color": value = !machines && !settings && !history ? "96,96,96,255" : "64,64,64,255"; return true;
                case "workshop_history_color": value = history ? "96,96,96,255" : "64,64,64,255"; return true;
                case "workshop_history_title": value = "COMPLETED (" + (c == null ? 0 : c.Completed.Count) + ")"; return true;
                case "workshop_machines_color": value = machines ? "96,96,96,255" : "64,64,64,255"; return true;
                case "workshop_settings_color": value = settings ? "96,96,96,255" : "64,64,64,255"; return true;
                case "workshop_add_ready": int quantity; value = (product != null && product.Unlocked && product.Present && amount != null
                    && int.TryParse(amount.Text, out quantity) && quantity > 0 && quantity <= WorkshopRules.MaximumTarget && WorkshopStore.Writable).ToString(); return true;
                case "workshop_add": value = product == null ? "SELECT AN ITEM" : !product.Unlocked ? "RECIPE LOCKED"
                    : !product.Present ? "NEEDS " + product.Station.ToUpperInvariant()
                    : (once ? "CRAFT " : "KEEP ") + (amount == null ? "" : amount.Text); return true;
                case "workshop_previous_ready": value = (resultPage > 0).ToString(); return true;
                case "workshop_next_ready": value = (resultPage < ResultPages() - 1).ToString(); return true;
                case "workshop_page_previous_ready": value = (page > 0).ToString(); return true;
                case "workshop_page_next_ready": value = (page < PageCount() - 1).ToString(); return true;
                case "workshop_mode_hint": int entered;
                    if (amount == null || !int.TryParse(amount.Text, out entered) || entered < 1 || entered > WorkshopRules.MaximumTarget)
                    { value = "Enter a whole quantity: 1 to " + WorkshopRules.MaximumTarget; return true; }
                    int needed; var availability = Preview(entered, out needed);
                    if (needed == 0) value = "Target already covered by stored and pending items.";
                    else if (availability == null) value = once ? "Make this many new items. Starts automatically."
                        : "Refill storage whenever stock falls below this amount.";
                    else if (availability.Maximum >= needed) value = "PLAN READY  /  MAX NOW " + availability.Maximum;
                    else value = "MAX NOW " + availability.Maximum + " / " + needed
                        + (string.IsNullOrEmpty(availability.Missing) ? "" : "  /  MISSING " + availability.Missing);
                    return true;
                case "workshop_selected": value = product == null ? "No matching items" : product.Title; return true;
                case "workshop_recipe": value = product == null ? "Try a different search." : product.Hint; return true;
                case "workshop_selected_icon": value = product == null ? "" : product.Icon; return true;
                case "workshop_selected_stock": value = product == null ? "" : Stored(product.Name) + " in storage / " + Pending(product.Name) + " in machines"; return true;
                case "workshop_results": value = products.Count + (showAllRecipes || (search != null && !string.IsNullOrWhiteSpace(search.Text)) ? " recipes" : " ready")
                    + " / page " + (resultPage + 1) + " of " + ResultPages(); return true;
                case "workshop_fuel": value = c != null && c.AutoFuel ? "ON" : "OFF"; return true;
                case "workshop_autocraft": value = c != null && c.AutoCraft ? "ON" : "OFF"; return true;
                case "workshop_run": value = c != null && c.Enabled ? "PAUSE ALL" : "RESUME"; return true;
                case "workshop_state": value = c != null && c.Enabled ? "AUTOMATION ON" : "AUTOMATION PAUSED"; return true;
                case "workshop_state_color": value = c != null && c.Enabled ? "135,205,155,255" : "235,183,99,255"; return true;
                case "workshop_page": value = (machines ? "MACHINES " : history ? "HISTORY " : "JOBS ") + (page + 1) + " / " + PageCount(); return true;
                case "workshop_list_hint": value = history ? "Last 60 requests / Repeat fills the form"
                    : machines ? "Live native queues, smelting inputs and automatic assignments"
                    : "Rolling batches start as soon as their materials are ready"; return true;
                case "workshop_info_visible": value = (machines || history).ToString(); return true;
                case "workshop_overview":
                    int working = stations.Count(s => MachinePhase(s, c) == "WORKING");
                    int attention = c == null ? 0 : c.Targets.Count(t => Phase(t, c) == "NEEDS SUPPLIES");
                    value = working + " working   /   " + stations.Count + " machines   /   " + (network == null ? 0 : network.ConnectedStorageCount) + " chests"
                        + (attention > 0 ? "   /   " + attention + " need supplies" : "");
                    return true;
                case "workshop_status":
                    if (Time.realtimeSinceStartup < noticeUntil) value = notice;
                    else if (!WorkshopStore.Writable) value = "Settings error. Check workshops.json and the game log.";
                    else if (!NearbyCraftMod.Config.Enabled) value = "NearbyCraft is disabled in config.json.";
                    else if (c == null || !c.Enabled) value = "Requesting an item starts automation. Queued work still finishes while paused.";
                    else if (!WorkshopManager.Status.TryGetValue(position, out value)) value = "Checking the workshop...";
                    return true;
            }
            for (int i = 0; i < WorkshopRules.VisibleRows; i++)
            {
                int result = resultPage * WorkshopRules.VisibleRows + i;
                var choice = result < products.Count ? products[result] : null;
                if (bindingName == "workshop_result_visible" + i) { value = (choice != null).ToString(); return true; }
                if (bindingName == "workshop_result_name" + i) { value = choice == null ? "" : choice.Title; return true; }
                if (bindingName == "workshop_result_icon" + i) { value = choice == null ? "" : choice.Icon; return true; }
                if (bindingName == "workshop_result_hint" + i) { value = choice == null ? "" : !choice.Unlocked ? "Recipe locked" : choice.Present ? choice.Station : "Needs " + choice.Station; return true; }
                if (bindingName == "workshop_result_color" + i) { value = result == selected ? "96,96,96,255" : "64,64,64,255"; return true; }
                int row = Row(i);
                var target = !machines && !settings && !history ? ActiveTarget(c, row) : null;
                var completed = history && c != null && row < c.Completed.Count ? c.Completed[row] : null;
                var station = machines && row < stations.Count ? stations[row] : null;
                if (station != null && (world.GetTileEntity(station.ToWorldPos()) != station || !WorkshopManager.Accessible(station))) station = null;
                bool enabled = station != null && c != null && !c.ExcludedStations.Contains(station.ToWorldPos().ToString());
                string phase = completed != null ? "DONE" : target != null ? Phase(target, c) : MachinePhase(station, c);
                if (bindingName == "workshop_row_visible" + i) { value = (target != null || station != null || completed != null).ToString(); return true; }
                if (bindingName == "workshop_name" + i) { value = completed != null ? DisplayName(completed.Item) : target != null ? DisplayName(target.Item) : station == null ? "" : DisplayName(station.block.GetBlockName()) + " #" + (stations.Take(row + 1).Count(s => s.block.GetBlockName() == station.block.GetBlockName())); return true; }
                if (bindingName == "workshop_icon" + i) { value = completed != null ? ItemIcon(completed.Item) : target != null ? ItemIcon(target.Item) : station == null ? "" : station.block.GetIconName(); return true; }
                if (bindingName == "workshop_phase" + i) { value = phase; return true; }
                if (bindingName == "workshop_phase_color" + i) { value = StateColor(phase); return true; }
                if (bindingName == "workshop_toggle" + i) { value = completed != null ? "Repeat" : target != null ? target.Enabled ? "Pause" : "Resume" : enabled ? "Disable" : "Enable"; return true; }
                if (bindingName == "workshop_eta" + i || bindingName == "workshop_eta_detail" + i)
                {
                    WorkshopCompletionEstimate estimate;
                    value = target == null ? "" : !target.Enabled || !c.Enabled ? "ETA: paused"
                        : !estimates.TryGetValue(target.Item, out estimate) ? "ETA: waiting"
                        : bindingName == "workshop_eta_detail" + i ? estimate.Label + "\n" + estimate.Waiting + "\n" + estimate.Detail
                        : estimate.Label;
                    return true;
                }
                if (bindingName == "workshop_goal" + i)
                {
                    value = completed != null ? completed.VerifiedDelivery ? "Collected " + completed.Produced + " / Requested " + completed.Requested
                            : "Requested " + completed.Requested + " / Legacy record"
                        : target != null ? target.Once ? target.TrackDelivery ? "Collected " + target.Returned + " / " + target.Target + "     Remaining " + target.Remaining
                            : "Remaining " + target.Remaining + " / In machines " + Pending(target.Item)
                        : "In storage " + Stored(target.Item) + " / Target " + target.Target
                        : station == null ? "" : "Position " + station.ToWorldPos();
                    return true;
                }
                if (bindingName == "workshop_detail" + i || bindingName == "workshop_detail_full" + i)
                {
                    string detail;
                    if (completed != null) detail = new DateTime(completed.UtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("dd MMM HH:mm")
                        + (completed.CollectedManually > 0 ? " / " + completed.CollectedManually + " collected manually" : " / Returned to storage")
                        + " / Repeat to prepare another request";
                    else if (target != null) detail = Detail(target);
                    else if (station == null) detail = "";
                    else if (!enabled) detail = "Excluded from automatic inputs, fuel and collection.";
                    else
                    {
                        var job = c == null ? null : c.Smelting.Find(j => j.Position == station.ToWorldPos());
                        if (job != null && station is TileEntityWorkstation)
                        {
                            var machine = (TileEntityWorkstation)station;
                            var jobItem = ItemClass.GetItem(job.Item, false);
                            long queuedNow = jobItem == null || jobItem.IsEmpty() ? 0 : WorkshopManager.CountQueued(machine, jobItem.type);
                            detail = queuedNow > 0 ? "Crafting " + DisplayName(job.Item) + " x" + queuedNow + " / " + job.Count + " more smelting"
                                : "Smelting inputs for " + DisplayName(job.Item) + " x" + job.Count;
                        }
                        else detail = station is TileEntityWorkstation ? WorkshopMachines.Describe((TileEntityWorkstation)station) : WorkshopCollectors.Describe((TileEntityCollector)station);
                        string state;
                        if (WorkshopManager.StationStatus.TryGetValue(station.ToWorldPos(), out state) && !string.IsNullOrEmpty(state)
                            && !state.StartsWith("Crafting ") && !(job != null && state.StartsWith("Smelting for "))) detail += " / " + state;
                    }
                    value = bindingName == "workshop_detail_full" + i ? detail : CompactDetail(detail);
                    return true;
                }
            }
            return base.GetBindingValueInternal(ref value, bindingName);
        }
    }

    [HarmonyPatch(typeof(BlockCompositeTileEntity), nameof(BlockCompositeTileEntity.OnBlockActivated))]
    internal static class WorkshopActivationPatch
    {
        private static bool Prefix(string _commandName, WorldBase _world, Vector3i _blockPos, BlockValue _blockValue,
            EntityPlayerLocal _player, ref bool __result)
        {
            if (!WorkshopManager.IsController(_blockValue.Block) || (_commandName != "TEFeatureStorage:Search" && _commandName != "Search")) return true;
            __result = false;
            if (!NearbyCraftMod.CanUseLocalStorage)
            { GameManager.ShowTooltip(_player, "Workshop Automation is available in solo/local worlds only."); return false; }
            if (!WorkshopManager.Accessible(_world.GetTileEntity(_blockPos)))
            { GameManager.ShowTooltip(_player, "Workshop Controller is locked."); return false; }
            var ui = LocalPlayerUI.GetUIForPlayer(_player);
            var group = ui == null ? null : ui.xui.FindWindowGroupByName(WorkshopManager.WindowGroupId) as XUiC_WorkshopWindowGroup;
            if (group == null) { Log.Error("[NearbyCraft] Workshop window is missing."); return false; }
            _player.AimingGun = false;
            group.SetPosition(_blockPos);
            ui.windowManager.Open(WorkshopManager.WindowGroupId, true);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockCompositeTileEntity), nameof(BlockCompositeTileEntity.OnBlockRemoved))]
    internal static class WorkshopRemovalPatch
    {
        private static void Postfix(Vector3i _blockPos, BlockValue _blockValue)
        {
            // Upgrading a console calls OnBlockRemoved too. Preserve its orders
            // when the new block at the same location is another console tier.
            if (WorkshopManager.IsManager(_blockValue.Block) && NearbyCraftMod.CanUseLocalStorage)
                WorkshopManager.Removed.Add(_blockPos);
        }
    }
}
