using NearbyCraft;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
void Define(int id, string name, string category = null, int weight = 1, int fuel = 0, int max = 30000)
    => ItemClass.Registry[id] = new ItemClass { Id = id, Name = name, Weight = weight, Fuel = fuel, MaxCount = max, MadeOfMaterial = new MaterialBlock { ForgeCategory = category } };
Define(1, "resourceWood", fuel: 10); Define(2, "resourceRockSmall", "stone"); Define(3, "unit_stone", "stone");
Define(4, "cement"); Define(5, "sand"); Define(6, "concrete"); Define(7, "unit_iron", "iron");
Define(8, "resourceScrapIron", "iron", 5); Define(9, "unit_clay", "clay"); Define(10, "resourceClayLump", "clay");
Define(11, "ironProduct"); Define(12, "tool");
Define(13, "unit_glass", "glass"); Define(14, "resourceCrushedSand", "glass", 4);
Define(15, "resourceForgedIron"); Define(16, "resourceMechanicalParts"); Define(17, "resourceOil");
Define(18, "toolForgeCrucible", max: 1);
ItemStack S(int type, int count) => new(new ItemValue(type), count);
Recipe R(int type, string area, bool material, params ItemStack[] inputs) => new() { itemValueType = type, count = 1, craftingArea = area, materialBasedRecipe = material, ingredients = inputs.ToList() };
XUiM_Recipes.All.Add(R(4, "forge", true, S(3, 1)));
XUiM_Recipes.All.Add(R(5, "mixer", false, S(2, 1)));
XUiM_Recipes.All.Add(R(6, "mixer", false, S(4, 1), S(5, 1), S(2, 1)));
XUiM_Recipes.All.Add(R(11, "forge", true, S(7, 3), S(9, 1)));
XUiM_Recipes.All.Add(R(14, "forge", true, S(13, 4)));
XUiM_Recipes.All.Add(R(14, "mixer", false, S(2, 1)));
XUiM_Recipes.All.Add(R(15, "forge", true, S(7, 10), S(9, 5)));
XUiM_Recipes.All.Add(R(18, "workbench", false, S(15, 100), S(16, 20), S(2, 1200), S(17, 20), S(10, 900)));
XUiM_Recipes.All.Last().craftingTime = 300;
var temporary = Directory.CreateTempSubdirectory("nearbycraft-scheduler-");
try
{
    var world = GameManager.Instance.World;
    var controllerPos = new Vector3i(0, 0, 0);
    var stations = new List<TileEntityWorkstation>();
    ItemStack[] chest = Array.Empty<ItemStack>();
    StorageNetworkSession network = null;
    int testWorld = 0;
    void Reset(int forges = 3, int mixers = 0)
    {
        world.Devices.Clear(); stations.Clear(); GamePrefs.SaveName = "Case" + testWorld++;
        WorkshopStore.Initialize(temporary.FullName);
        WorkshopStore.Edit(controllerPos, c => { c.Enabled = true; c.Linked = true; c.ConsoleX = c.ConsoleY = c.ConsoleZ = 0; }, out _);
        chest = ItemStack.CreateArray(24); chest[0] = S(2, 1000); chest[1] = S(1, 1000);
        chest[2] = S(8, 1000); chest[3] = S(10, 1000);
        network = new StorageNetworkSession(world, chest);
        for (int i = 0; i < forges + mixers; i++)
        {
            bool forge = i < forges;
            var station = new TileEntityWorkstation { Position = new Vector3i(i + 1, 0, 0) };
            station.block.Name = forge ? "forge" : "mixer";
            station.isModuleUsed[(int)TileEntityWorkstation.Module.Output] = true;
            station.isModuleUsed[(int)TileEntityWorkstation.Module.Fuel] = forge;
            station.isModuleUsed[(int)TileEntityWorkstation.Module.Material_Input] = forge;
            if (forge) station.Input = new[] { S(0, 0), S(0, 0), S(0, 0), S(3, 0), S(7, 0), S(9, 0) };
            world.Devices[station.Position] = station; stations.Add(station);
        }
    }
    WorkshopControllerData C() => WorkshopStore.Get(controllerPos);
    void Order(string item, int count, bool once = true) => WorkshopStore.Edit(controllerPos,
        c => c.Targets.Add(new WorkshopTarget { Item = item, Target = count, Remaining = once ? count : 0, Once = once, TrackDelivery = once }), out _);
    void Tick() => new WorkshopScheduler(network, world.GetPrimaryPlayer(), new XUi(), C(), stations).Run();
    long Count(int type) => chest.Where(s => s.itemValue.type == type).Sum(s => (long)s.count);
    void Advance()
    {
        foreach (var station in stations)
        {
            if (WorkshopMachines.Uses(station, TileEntityWorkstation.Module.Material_Input))
                for (int i = 0; i < station.InputSlotCount; i++)
                {
                    var raw = station.Input[i]; if (raw.IsEmpty()) continue;
                    var unit = station.Input.Skip(station.InputSlotCount).First(s => s.itemValue.ItemClass.MadeOfMaterial.ForgeCategory == raw.itemValue.ItemClass.MadeOfMaterial.ForgeCategory);
                    unit.count += raw.count * raw.itemValue.ItemClass.GetWeight(); station.Input[i] = S(0, 0);
                }
            for (int i = 0; i < station.Queue.Length; i++)
            {
                var q = station.Queue[i]; if (q?.Recipe == null) continue;
                var plan = new StorageTransferPlan(); plan.Add(station.Output, new bool[station.Output.Length]);
                Check(plan.Deposit(S(q.Recipe.itemValueType, q.Multiplier * q.Recipe.count)) == q.Multiplier * q.Recipe.count && plan.TryCommit(_ => true), "Simulated native output fits");
                station.Queue[i] = null;
            }
            network.CollectOutput(station, (type, count) => WorkshopStore.CreditDelivery(C(), ItemClass.GetForId(type).Name, count));
        }
    }

    TileEntityWorkstation CrucibleSetup(int iron = 100, int clay = 900)
    {
        Reset(2, 1);
        var bench = stations.Single(s => s.block.Name == "mixer");
        bench.block.Name = "workbench";
        chest[0] = S(2, 1200); chest[3] = S(10, clay);
        chest[4] = S(15, iron); chest[5] = S(16, 20); chest[6] = S(17, 20);
        Order("toolForgeCrucible", 1);
        return bench;
    }

    var crucibleBench = CrucibleSetup(); Tick();
    Check(crucibleBench.Queue.Last()?.Recipe?.itemValueType == 18 && C().Targets[0].Remaining == 0,
        "A supplied crucible goes straight to the workbench with AutoCraft enabled");
    Check(C().Smelting.Count == 0 && stations.Where(s => s != crucibleBench).All(s => !s.hasRecipeInQueue()),
        "A supplied crucible does not schedule unnecessary forge components");
    Check(Count(15) == 0 && Count(16) == 0 && Count(2) == 0 && Count(17) == 0 && Count(10) == 0,
        "Crucible queue consumes its five actual workbench ingredients exactly once");
    Advance(); Tick();
    Check(Count(18) == 1 && C().Completed.Count == 1, "Crucible completion returns its non-stackable output to storage");

    crucibleBench = CrucibleSetup();
    chest[4].itemValue.Seed = 321; chest[4].itemValue.Flags = 2;
    chest[5].itemValue.Metadata = 77;
    Tick();
    Check(crucibleBench.hasRecipeInQueue() && C().Smelting.Count == 0 && Count(15) == 0,
        "Craft payment accepts existing components by the native ingredient rule, regardless of transfer-only metadata");
    crucibleBench = CrucibleSetup();
    chest[4].itemValue.HasModSlots = chest[4].itemValue.InstalledMods = true;
    Tick();
    Check(!crucibleBench.hasRecipeInQueue() && Count(15) == 100,
        "Components with installed mods are not consumed or claimed as usable ingredients");
    crucibleBench = CrucibleSetup(); network.Locks[4] = true; Tick();
    Check(!crucibleBench.hasRecipeInQueue() && Count(15) == 100, "Locked components stay outside automatic crafting");

    crucibleBench = CrucibleSetup(0, 1400); Tick();
    Check(!crucibleBench.hasRecipeInQueue() && C().Smelting.Sum(j => j.Count) == 100
        && C().Smelting.All(j => j.Item == "resourceForgedIron" && j.Owner == "toolForgeCrucible"),
        "A crucible missing forged iron schedules exactly that prerequisite on forges");
    Check(Count(2) == 1200 && Count(10) == 900 && Count(16) == 20 && Count(17) == 20,
        "Forge preparation preserves the crucible's final stone, clay, parts and oil");
    for (int i = 0; i < 6; i++) { Advance(); Tick(); }
    Check(Count(18) == 1 && Count(15) == 0 && C().Smelting.Count == 0 && C().Completed.Count == 1,
        "The crucible chain hands forge output to the workbench and completes once");

    crucibleBench = CrucibleSetup(0); C().AutoCraft = false; Tick();
    Check(!crucibleBench.hasRecipeInQueue() && C().Smelting.Count == 0 && Count(10) == 900,
        "Disabling craft-missing-components leaves an unsupplied crucible waiting without forge work");

    crucibleBench = CrucibleSetup(); crucibleBench.Busy = true; Tick();
    Check(C().Smelting.Count == 0 && stations.All(s => !s.hasRecipeInQueue()),
        "An inaccessible workbench cannot redirect a supplied crucible to a forge");

    crucibleBench = CrucibleSetup(60, 1100); Tick();
    Check(C().Smelting.Sum(j => j.Count) == 40 && Count(15) == 60 && Count(10) == 900,
        "A partially supplied crucible makes only the 40 missing forged iron and protects its clay");

    crucibleBench = CrucibleSetup(60);
    var ironRecipe = XUiM_Recipes.All.Single(r => r.itemValueType == 15);
    stations[0].Queue[^1] = new RecipeQueueItem { Recipe = ironRecipe, Multiplier = 40,
        OneItemCraftTime = 10, CraftingTimeLeft = 7, IsCrafting = true };
    Tick();
    Check(C().Smelting.Count == 0 && !stations[1].hasRecipeInQueue(),
        "Iron already being made satisfies the crucible's shortage without duplicate forge work");
    string preparingCrucible = WorkshopManager.TargetStatus[WorkshopManager.TargetKey(controllerPos, "toolForgeCrucible")];
    Check(preparingCrucible.Contains("Preparing") && preparingCrucible.Contains("resourceForgedIron"),
        "The crucible reports waiting for its queued component, not a false missing-supplies state: " + preparingCrucible);

    // A faster alternate recipe must not outrank one whose finished components
    // are already on hand and send the job back down a dependency chain.
    CrucibleSetup();
    var alternateCrucible = R(18, "workbench", false, S(11, 100)); alternateCrucible.craftingTime = 1;
    XUiM_Recipes.All.Add(alternateCrucible); Tick();
    Check(stations.Last().Queue.Last()?.Recipe?.craftingTime == 300 && C().Smelting.Count == 0,
        "Available final components take priority over a faster recipe requiring component production");
    XUiM_Recipes.All.Remove(alternateCrucible);

    crucibleBench = CrucibleSetup(60);
    stations[0].Queue[^1] = new RecipeQueueItem { Recipe = ironRecipe, Multiplier = 40,
        OneItemCraftTime = 10, CraftingTimeLeft = 7, IsCrafting = true };

    Dictionary<string, WorkshopCompletionEstimate> Estimates() => new WorkshopCompletionEstimator(network,
        world.GetPrimaryPlayer(), new XUi(), C(), stations).Estimate();
    void Seconds(string item, double expected, string message)
    {
        var estimate = Estimates()[item];
        Check(estimate.Seconds.HasValue && Math.Abs(estimate.Seconds.Value - expected) < .01,
            message + ": expected " + expected + ", got " + estimate.Seconds + " " + estimate.Waiting);
    }
    // 40 iron: 7 seconds to the first, then 39 * 10; collection,
    // followed by the 300-second workbench recipe and final collection.
    Seconds("toolForgeCrucible", 701, "ETA includes the existing ingredient queue before final crafting");
    CrucibleSetup();
    Seconds("toolForgeCrucible", 302, "Supplied crucible ETA uses the workbench recipe's full time");
    stations.Last().CraftSpeed = .5f;
    Seconds("toolForgeCrucible", 152, "ETA applies the chosen station's staged crafting speed");
    C().Targets[0].Enabled = false;
    Check(!Estimates()["toolForgeCrucible"].Seconds.HasValue, "Paused jobs have no running ETA");
    CrucibleSetup(60);
    Check(!Estimates()["toolForgeCrucible"].Seconds.HasValue, "Missing clay prevents a fabricated full-job ETA");

    Reset(0, 1); Order("sand", 3);
    Tick(); stations[0].Queue[^1].CraftingTimeLeft = 7;
    Seconds("sand", 29, "Native ETA uses remaining first-item time plus the subsequent items");
    stations[0].Busy = true;
    Check(!Estimates()["sand"].Seconds.HasValue, "An accessed machine reports waiting");
    stations[0].Busy = false; stations[0].Queue[^1].CraftingTimeLeft = float.NaN;
    Check(!Estimates()["sand"].Seconds.HasValue, "An invalid native timer cannot create a numeric ETA");

    Reset(0, 2); Order("sand", 10);
    Seconds("sand", 52, "Ten crafts on two equal machines finish in five craft times");
    var sameKeyRecipe = R(5, "mixer", false, S(2, 1)); sameKeyRecipe.craftingTime = 5;
    XUiM_Recipes.All.Add(sameKeyRecipe);
    Seconds("sand", 27, "Alternate recipes on one station cannot create a second phantom machine");
    XUiM_Recipes.All.Remove(sameKeyRecipe);
    stations[1].CraftSpeed = .5f;
    Seconds("sand", 37, "Unequal machine speeds allocate integral batches to minimize completion time");
    stations[0].Queue[^1] = new RecipeQueueItem { Recipe = XUiM_Recipes.All.Single(r => r.itemValueType == 5),
        Multiplier = 10, OneItemCraftTime = 10, CraftingTimeLeft = 10 };
    Seconds("sand", 52, "A busy machine does not delay work that fits the free faster machine");

    Reset(0, 1); chest[4] = S(4, 20); chest[5] = S(5, 20);
    Order("sand", 3); Order("concrete", 2);
    var sharedEtas = Estimates();
    Check(sharedEtas["sand"].Seconds == 32 && sharedEtas["concrete"].Seconds == 52,
        "Separate jobs share a machine calendar instead of both claiming an idle machine");

    Reset(1); Order("cement", 9);
    Seconds("cement", 95, "Forge ETA includes three parallel smelting lanes and nine crafts");
    string etaStateBefore = Newtonsoft.Json.JsonConvert.SerializeObject(new { chest, stations, controller = C() });
    Estimates(); Estimates();
    Check(etaStateBefore == Newtonsoft.Json.JsonConvert.SerializeObject(new { chest, stations, controller = C() }),
        "ETA evaluation never changes native queues, timers, tools, inputs, fuel, storage or progress");
    Tick();
    Seconds("cement", 95, "An existing smelting assignment is included once in the ETA");
    C().AutoFuel = false; stations[0].IsBurning = false;
    Check(!Estimates()["cement"].Seconds.HasValue, "A stopped forge with automatic fuel off has no finite ETA");
    Reset(1); Order("cement", 9); chest[1] = S(0, 0);
    Check(!Estimates()["cement"].Seconds.HasValue, "ETA requires fuel for the whole planned work");
    Reset(0, 1); Order("sand", 100000); chest[0].count = 100000;
    var etaClock = System.Diagnostics.Stopwatch.StartNew();
    Seconds("sand", 1000002, "Large jobs include all native batches without overflow");
    Check(etaClock.ElapsedMilliseconds < 2000, "Large ETA calculation stays bounded");

    Reset();
    WorkshopManager.PreparedRecipes = 0;
    Tick();
    Check(WorkshopManager.PreparedRecipes == 0, "Idle controller skips recipe staging without outstanding demand");
    Order("cement", 1); stations[2].Input[3].count = 9;
    Tick();
    Check(stations[2].hasRecipeInQueue() && !stations[0].hasRecipeInQueue() && C().Smelting.Count == 0, "Uses farther preloaded forge before empty nearest forges");
    Check(Count(2) == 1000 && C().Targets[0].Remaining == 0, "Preloaded material avoids unnecessary raw feed and duplicate assignments");

    Reset(1, 1); chest[0] = S(2, 30);
    WorkshopManager.PreparedRecipes = 0;
    var productionPlanner = new WorkshopProductionPlanner(network, world.GetPrimaryPlayer(), new XUi(), C(), stations, true);
    int allRecipePreparations = WorkshopManager.PreparedRecipes;
    WorkshopManager.PreparedRecipes = 0;
    var focusedPlanner = new WorkshopProductionPlanner(network, world.GetPrimaryPlayer(), new XUi(), C(), stations,
        true, new[] { 6 });
    Check(WorkshopManager.PreparedRecipes < allRecipePreparations,
        "Demand-scoped planner avoids preparing unrelated workstation recipes");
    var reservationBank = new WorkshopReservationBank();
    var concretePlan = productionPlanner.Plan(6, 100, productionPlanner.CreatePool(), reservationBank);
    var focusedPlan = focusedPlanner.Plan(6, 100, focusedPlanner.CreatePool(), new WorkshopReservationBank());
    Check(focusedPlan.Maximum == concretePlan.Maximum && focusedPlan.Missing == concretePlan.Missing,
        "Demand-scoped planner preserves the full dependency plan");
    Check(concretePlan.Maximum == 10 && concretePlan.Missing.Contains("resourceRockSmall"),
        "Whole-job preview reports the true maximum and missing base component across shared dependencies");
    Check(reservationBank.Reserved(2) == 30,
        "Whole-job preview reserves sand, cement and final concrete stone before execution");
    Order("concrete", 100); Tick();
    Check(Count(2) == 10 && C().Smelting.Count == 1 && stations.Single(s => s.block.Name == "mixer").hasRecipeInQueue(),
        "Sibling dependency execution preserves the stone reserved for final concrete");

    Reset(1, 1); chest[0] = S(2, 20); Order("resourceCrushedSand", 20); Tick();
    Check(stations.Single(s => s.block.Name == "mixer").hasRecipeInQueue()
        && !stations.Single(s => s.block.Name == "forge").Input.Take(3).Any(s => s.itemValue.type == 14 && !s.IsEmpty())
        && C().Smelting.Count == 0,
        "Planner rejects the vanilla sand-to-glass-to-sand forge loop and uses the productive mixer recipe");

    Reset(); Order("cement", 24); Tick();
    Check(C().Smelting.Count == 3 && C().Smelting.Sum(j => j.Count) == 24, "Splits one order across three forges without duplicate reservations");
    Check(C().Smelting.Max(j => j.Count) - C().Smelting.Min(j => j.Count) <= 1, "Empty forges receive balanced batches");
    long rawAfter = Count(2); Tick(); Tick();
    Check(Count(2) == rawAfter && C().Smelting.Sum(j => j.Count) == 24, "Repeated ticks do not repeat pending smelting demand");
    WorkshopStore.Initialize(temporary.FullName); Tick();
    Check(Count(2) == rawAfter && C().Smelting.Count == 3, "Settings reload preserves forge ownership and demand reservations");
    for (int i = 0; i < 5; i++) { Advance(); Tick(); }
    Check(Count(4) == 24 && C().Targets[0].Remaining == 0 && C().Smelting.Count == 0, "Parallel smelting finishes exact requested total");

    Reset(1); Order("cement", 8); Tick();
    var rollingForge = stations[0];
    rollingForge.Input[0].count -= 2;
    rollingForge.Input[3].count = 2;
    Tick();
    Check(rollingForge.Queue.Last().Multiplier == 2 && C().Smelting.Single().Count == 6
        && C().Targets[0].Remaining == 6 && C().Targets[0].Queued == 2,
        "Forge queues the ready partial chunk instead of waiting for its whole smelting allocation");
    Tick();
    Check(C().Smelting.Single().Count == 6 && rollingForge.Queue.Last().Multiplier == 2,
        "Busy rolling forge retains exactly the unqueued assignment remainder");
    Advance(); Tick();
    Check(rollingForge.Queue.Last().Multiplier == 6 && C().Smelting.Count == 0
        && C().Targets[0].Remaining == 0 && C().Targets[0].Queued == 8,
        "Rolling forge queues the remaining chunk after native completion without duplication");

    Reset(); Order("cement", 1); Tick();
    Check(C().Smelting.Count == 1 && Count(2) == 999, "Tiny order does not spread duplicate single batches");

    Reset(); Order("ironProduct", 10); chest[3] = S(0, 0); Tick();
    Check(C().Smelting.Count == 0 && Count(8) == 1000 && stations.All(s => s.Input.Take(3).All(i => i.IsEmpty())), "Missing clay prevents speculative iron smelting on every forge");
    chest[3] = S(10, 10); Tick();
    Check(C().Smelting.Count > 1 && C().Smelting.Sum(j => j.Count) == 10, "Supplying missing ingredient automatically resumes balanced assignments");

    Reset(2); Order("cement", 100); Order("ironProduct", 100); Tick();
    Check(C().Smelting.Select(j => j.Owner).Distinct().Count() == 2, "First pass gives competing orders a forge before filling spare capacity");

    Reset(1); chest[0] = S(2, 12); Order("cement", 12); Tick();
    WorkshopManager.PreparedRecipes = 0; Tick();
    Check(C().Smelting.Sum(j => j.Count) == 12 && Count(2) == 0
        && WorkshopManager.PreparedRecipes == 1,
        "Already assigned material is not planned or reserved a second time on every tick");

    Reset(2); Order("cement", 1); stations[1].Input[0] = S(2, 10); Tick();
    Check(C().Smelting.Single().Position == stations[1].Position && Count(2) == 1000, "Uses already-smelting matching input in farther forge");
    C().Targets.Clear(); Tick();
    Check(C().Smelting.Count == 0 && stations[1].Input.Take(3).Sum(s => s.count) == 10, "Removing order releases assignment without deleting real material");

    Reset(3, 1); Order("concrete", 20); Tick();
    Check(stations.Any(s => s.block.Name == "mixer" && s.hasRecipeInQueue()) && C().Smelting.Count > 1, "Even one busy mixer can request other intermediates from multiple forges concurrently");
    for (int i = 0; i < 30; i++) { Advance(); Tick(); }
    Check(Count(6) == 20 && C().Targets[0].Remaining == 0, "Dependency chain completes through parallel native queues");

    Reset(0, 1); chest[0] = S(2, 5000); chest[4] = S(4, 5000); chest[5] = S(5, 5000); Order("concrete", 2000); Tick();
    Check(network.ProductSnapshotScans <= 3,
        "Large candidate batch search reuses a small number of whole-network snapshots");
    Check(stations[0].Queue.Last().Multiplier == 2000 && C().Targets[0].Remaining == 0 && C().Targets[0].Queued == 2000,
        "Large mixer order enters one capacity-safe native batch instead of ten-item waves");
    Check(Count(2) == 3000 && Count(4) == 3000 && Count(5) == 3000,
        "Large adaptive queue pays every ingredient exactly once");

    Reset(0, 1); Order("sand", 1000);
    stations[0].Output = Enumerable.Range(0, 6).Select(i => i == 0 ? S(5, 29990) : S(2, 30000)).ToArray(); Tick();
    Check(stations[0].Queue.Last().Multiplier == 10 && C().Targets[0].Remaining == 990,
        "Adaptive batch contracts to the actual mixer output room");

    Reset(0, 1); Order("concrete", 1000);
    var activeOrder = C().Targets[0];
    activeOrder.Remaining = 970; activeOrder.Queued = 30; activeOrder.Returned = 22;
    chest[4] = S(4, 1000); chest[5] = S(5, 1000);
    var concreteRecipe = XUiM_Recipes.All.First(r => r.itemValueType == 6);
    stations[0].Queue[^1] = new RecipeQueueItem { Recipe = concreteRecipe, Multiplier = 8, OneItemCraftTime = 1, CraftingTimeLeft = 1, IsCrafting = true };
    Tick();
    string activeStatus = WorkshopManager.TargetStatus[WorkshopManager.TargetKey(controllerPos, "concrete")];
    Check(activeStatus == "Crafting 8 items / Next batch starts when the current machine is free",
        "Busy single-mixer orders report active work instead of a false idle wait: " + activeStatus);
    Check(WorkshopScheduler.DescribeActiveProgress(0, 8, 970, "Waiting for a free machine")
        == "8 finished awaiting collection / Next batch starts after output collection",
        "Finished output is distinguished from an occupied crafting queue");

    Reset(); Order("cement", 12, false); Tick();
    chest[4] = S(4, 12); Tick();
    Check(C().Smelting.Count == 0 && stations.All(s => !s.hasRecipeInQueue()), "Externally satisfied stock demand releases unnecessary unqueued assignments");

    Reset(0, 1); chest[0] = S(0, 0); Order("sand", 5); Tick();
    Check(!stations[0].hasRecipeInQueue(), "Missing storage stock blocks a candidate without speculative queueing");
    chest[0] = S(2, 5); Tick();
    Check(stations[0].Queue.Last().Multiplier == 5 && Count(2) == 0,
        "A new scheduler tick observes externally added stock and still pays through the live transaction");

    Reset(); Order("cement", 20); Tick();
    var removed = stations[0]; stations.RemoveAt(0); world.Devices.Remove(removed.Position); Tick();
    Check(C().Smelting.All(j => j.Position != removed.Position), "Removed forge is released and remaining work can be reassigned");
    for (int i = 0; i < 10; i++) { Advance(); Tick(); }
    Check(Count(4) == 20, "Surviving forges finish the order after one forge is removed");

    Reset(1); Order("cement", 8); Tick();
    Check(stations[0].Input.Take(3).Select(s => s.count).SequenceEqual(new[] { 3, 3, 2 }), "One material fills all independent native lanes evenly");
    Check(C().Completed.Count == 0 && C().Targets[0].Queued == 0, "Smelting alone never marks an order complete");
    Advance(); Tick();
    Check(C().Completed.Count == 0 && C().Targets[0].Queued == 8 && C().Targets[0].Returned == 0, "Queue submission is not delivery");
    // Simulate native completion without collecting output yet.
    stations[0].Output[0] = S(4, 8); stations[0].Queue = new RecipeQueueItem[4]; Tick();
    Check(C().Completed.Count == 0, "Output sitting inside a machine is not completed history");
    Advance(); Tick(); Tick();
    Check(C().Completed.Count == 1 && C().Completed[0].Produced == 8 && C().Targets[0].CompletedUtcTicks > 0,
        "Collected output creates one bounded completion receipt");
    WorkshopStore.Initialize(temporary.FullName); Tick();
    Check(C().Completed.Count == 1 && C().Completed[0].VerifiedDelivery, "Completion survives reload without duplicate history");

    Reset(1); Order("cement", 5); stations[0].Input[3].count = 5; Tick();
    stations[0].Queue = new RecipeQueueItem[4]; Tick();
    Check(C().Completed.Count == 0 && C().Targets[0].Returned == 0, "Manually cancelled native queue cannot fabricate a delivery receipt");

    Reset(0, 1); Order("sand", 8); Tick();
    stations[0].Queue = new RecipeQueueItem[4]; stations[0].Output[0] = S(5, 8);
    var outputBefore = WorkshopOutputAccounting.Capture(stations[0].Output);
    stations[0].Output[0] = S(5, 3); stations[0].Output[1] = S(5, 5);
    Check(!WorkshopOutputAccounting.CreditRemoved(C(), outputBefore, stations[0].Output),
        "Rearranging output stacks gives no collection credit");
    outputBefore = WorkshopOutputAccounting.Capture(stations[0].Output);
    stations[0].Output[1] = S(0, 0);
    Check(WorkshopOutputAccounting.CreditRemoved(C(), outputBefore, stations[0].Output)
        && C().Targets[0].Returned == 5 && C().Targets[0].CollectedManually == 5,
        "Manual pickup credits only the five items actually removed");
    Tick(); Check(C().Completed.Count == 0, "Partially collected output waits only for the remaining items");
    network.CollectOutput(stations[0], (type, count) => WorkshopStore.CreditDelivery(C(), ItemClass.GetForId(type).Name, count));
    Tick();
    Check(C().Completed.Count == 1 && C().Targets[0].Returned == 8 && C().Completed[0].CollectedManually == 5,
        "Mixed manual and automatic collection completes the order without waiting forever");
    WorkshopStore.Initialize(temporary.FullName);
    Check(C().Completed[0].CollectedManually == 5 && C().Targets[0].CollectedManually == 5,
        "Manual collection receipts survive reload");

    Reset(0, 1); Order("sand", 2); Tick();
    stations[0].Queue = new RecipeQueueItem[4]; stations[0].Output[0] = S(5, 2);
    outputBefore = WorkshopOutputAccounting.Capture(stations[0].Output);
    stations[0].Output[0] = S(0, 0);
    WorkshopOutputAccounting.CreditRemoved(C(), outputBefore, stations[0].Output); Tick();
    Check(C().Completed.Count == 1 && C().Completed[0].CollectedManually == 2,
        "Taking the entire output before the next scheduler tick completes the job");

    Reset(0, 2); Order("sand", 15); stations[1].CraftSpeed = .5f; Tick();
    Check(stations[1].Queue.Last().Multiplier == 10 && stations[0].Queue.Last().Multiplier == 5,
        "Twice-as-fast machine receives twice the work instead of an equal split");
    Check(stations[1].Queue.Last().OneItemCraftTime == 5 && stations[0].Queue.Last().OneItemCraftTime == 10,
        "Estimated machine-specific recipe time is also used by the actual queued batch");
    Reset(0, 2); Order("sand", 1); stations[1].CraftSpeed = .25f; Tick();
    Check(!stations[0].hasRecipeInQueue() && stations[1].hasRecipeInQueue(), "Small requests choose the farther faster compatible machine");
    Reset(0, 2); Order("sand", 5); stations[0].Busy = true; Tick();
    Check(!stations[0].hasRecipeInQueue() && stations[1].hasRecipeInQueue(), "A machine being accessed does not block independent free machines");
    Reset(0, 2); Order("sand", 5); stations[0].Output = Enumerable.Range(0, 6).Select(_ => S(2, 30000)).ToArray(); Tick();
    Check(!stations[0].hasRecipeInQueue() && stations[1].hasRecipeInQueue(), "Output-blocked machine does not absorb the available allocation");
    Reset(2); Order("cement", 1); C().AutoFuel = false;
    stations[1].Fuel[0] = S(1, 5); stations[1].IsBurning = true; Tick();
    Check(C().Smelting.Single().Position == stations[1].Position, "Manual-fuel mode assigns only to a fueled and lit forge");
    Reset(0, 1); Order("sand", 2);
    var fasterRecipe = R(5, "mixer", false, S(2, 1)); fasterRecipe.craftingTime = 1; XUiM_Recipes.All.Add(fasterRecipe); Tick();
    Check(stations[0].Queue.Last().OneItemCraftTime == 1, "Available faster recipe wins over catalog order on one machine");
    XUiM_Recipes.All.Remove(fasterRecipe);
    Reset(0, 1); Order("sand", 1);
    var bulkRecipe = R(5, "mixer", false, S(2, 10)); bulkRecipe.count = 10; bulkRecipe.craftingTime = 30; XUiM_Recipes.All.Add(bulkRecipe); Tick();
    Check(stations[0].Queue.Last().Recipe.count == 1, "Tiny request avoids a slower bulk recipe even when bulk has better throughput");
    Reset(0, 1); Order("sand", 20); Tick();
    Check(stations[0].Queue.Last().Recipe.count == 10 && stations[0].Queue.Last().Multiplier == 2, "Larger request can use the higher-throughput bulk recipe");
    Reset(0, 1); Order("sand", 1, false); Tick();
    Check(stations[0].Queue.Last().Recipe.count == 1, "Stock target below a bulk yield still finds the fitting alternative recipe");
    XUiM_Recipes.All.Remove(bulkRecipe);
    Reset(2); Order("cement", 1);
    stations[1].isModuleUsed[(int)TileEntityWorkstation.Module.Tools] = true;
    stations[1].Tools[0] = S(12, 1); ItemClass.GetForId(12).SmeltMultiplier = .2f;
    Tick(); Check(C().Smelting.Single().Position == stations[1].Position, "Faster native smelting tool wins over nearest forge");
    ItemClass.GetForId(12).SmeltMultiplier = 1;

    var layoutRng = new Random(1800);
    for (int example = 0; example < 400; example++)
    {
        Reset(1); var forge = stations[0]; int lanes = layoutRng.Next(1, 4);
        forge.InputSlotCount = lanes;
        forge.Input = ItemStack.CreateArray(lanes).Concat(new[] { S(3, 0), S(7, 0), S(9, 0) }).ToArray();
        int materials = layoutRng.Next(1, lanes + 1);
        var inputs = new[] { S(3, layoutRng.Next(1, 100)), S(7, layoutRng.Next(1, 100)), S(9, layoutRng.Next(1, 100)) }.Take(materials).ToArray();
        var recipe = R(4, "forge", true, inputs);
        Check(WorkshopForgePlan.Create(forge, recipe, 1, out var plan, out _), "Random lane plan fits all required materials");
        Check(forge.Input.Take(lanes).All(s => s.IsEmpty()), "Dry-run never changes native input stacks");
        foreach (var supply in plan.Supplies)
            Check(plan.Inputs.Take(lanes).Where(s => s.itemValue.type == supply.itemValue.type).Sum(s => s.count) == supply.count,
                "Random lane plan conserves every withdrawn raw item");
        Check(plan.Seconds > 0 && !double.IsNaN(plan.Seconds) && !double.IsInfinity(plan.Seconds), "Random lane plan has finite positive completion estimate");
        if (materials == 1)
            Check(plan.Inputs.Take(lanes).Max(s => s.count) - plan.Inputs.Take(lanes).Min(s => s.count) <= 1,
                "Single-material plan balances two-slot and three-slot forges");
    }
    Reset(1); var twoLane = stations[0]; twoLane.InputSlotCount = 2;
    twoLane.Input = new[] { S(0, 0), S(0, 0), S(3, 0), S(7, 0), S(9, 0) };
    Check(WorkshopForgePlan.Create(twoLane, R(11, "forge", true, S(7, 100), S(9, 2)), 1, out var twoPlan, out _)
        && twoPlan.Inputs[0].itemValue.type != twoPlan.Inputs[1].itemValue.type, "Iron cannot steal clay's second input slot");
    twoLane.Input[0] = S(8, 7); twoLane.Timers[0] = .25f;
    Check(WorkshopForgePlan.Create(twoLane, R(11, "forge", true, S(7, 40)), 1, out var existingPlan, out _)
        && existingPlan.Inputs[0].count == 4 && existingPlan.Inputs[1].count == 4 && twoLane.Timers[0] == .25f,
        "Existing unstarted tails and new supply share spare lanes without resetting the active item");

    Reset(1); Order("cement", 90); stations[0].Input[0] = S(2, 90); stations[0].Timers[0] = .1f;
    Tick();
    Check(stations[0].Input.Take(3).All(s => s.count == 30) && stations[0].Timers[0] == .1f
        && Count(2) == 1000, "Existing single-slot stock is balanced without extra withdrawals or lost progress");
    stations[0].Input[0].count = 88; stations[0].Input[1].count = 1; stations[0].Input[2].count = 1;
    Tick();
    Check(stations[0].Input.Take(3).All(s => s.count == 30), "Ongoing smelting assignments recover an uneven input layout");

    var rng = new Random(1700);
    for (int example = 0; example < 150; example++)
    {
        int forgeCount = rng.Next(1, 7), wanted = rng.Next(1, 150), yield = rng.Next(1, 5);
        Reset(forgeCount); XUiM_Recipes.All[0].count = yield;
        Order("cement", wanted);
        foreach (var forge in stations)
        {
            forge.Input[3].count = rng.Next(0, 8);
            if (rng.Next(2) == 0) forge.Input[0] = S(2, rng.Next(0, 8));
        }
        long startingStone = Count(2) + stations.Sum(s => s.Input.Take(3).Sum(i => i.count) + s.Input[3].count);
        for (int tick = 0; tick < 60; tick++)
        {
            Tick();
            Check(C().Smelting.Select(j => j.Position).Distinct().Count() == C().Smelting.Count, "Random: one assignment per forge");
            Check(C().Smelting.Sum(j => j.Count) <= C().Targets[0].Remaining + yield - 1, "Random: assignments cannot double-count order demand");
            Advance();
            if (C().Targets[0].Remaining == 0 && C().Smelting.Count == 0 && stations.All(s => !s.hasRecipeInQueue())) break;
        }
        long produced = ((wanted + yield - 1) / yield) * yield;
        Check(Count(4) == produced && C().Targets[0].Remaining == 0, "Random: exact requested whole-yield output");
        Check(startingStone == Count(2) + stations.Sum(s => s.Input.Take(3).Sum(i => i.count) + s.Input[3].count) + produced / yield, "Random: native units/raw materials conserved");
    }
    XUiM_Recipes.All[0].count = 1;
    if (args.Contains("--profile"))
    {
        File.Delete(Path.Combine(temporary.FullName, "workshops.json"));
        Reset(WorkshopRules.MaximumStations); chest[0].count = 100000; chest[1].count = 100000; Order("cement", 20000);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        long bytes = GC.GetAllocatedBytesForCurrentThread(); Tick();
        Console.WriteLine($"Profile: allocate 20,000 cement / {stations.Count} forges: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024d:F1} KiB");
        stopwatch.Restart(); bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 0; tick < 100; tick++) Tick();
        Console.WriteLine($"Profile: waiting smelting tick / {stations.Count} forges: {stopwatch.Elapsed.TotalMilliseconds / 100:F2} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 102400d:F1} KiB mean");
        stopwatch.Restart(); bytes = GC.GetAllocatedBytesForCurrentThread();
        var estimate = Estimates()["cement"];
        Console.WriteLine($"Profile: ETA / {stations.Count} forges: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024d:F1} KiB, {estimate.Label}");
    }
    Reset(); Order("cement", 20); Directory.CreateDirectory(Path.Combine(temporary.FullName, "workshops.json.tmp")); Tick();
    Check(!WorkshopStore.Writable && !C().Enabled && C().Targets[0].Remaining == 20, "Assignment persistence failure stops automation without reporting unsmelted work as crafted");
    Directory.Delete(Path.Combine(temporary.FullName, "workshops.json.tmp"));
    Console.WriteLine($"PASS: {checks} scheduler integration assertions (production scheduler/transactions/store; native API stubs).");
}
finally { temporary.Delete(true); }
