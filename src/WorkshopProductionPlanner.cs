using System;
using System.Collections.Generic;
using System.Linq;

namespace NearbyCraft
{
    internal sealed class WorkshopProductionStep
    {
        internal Recipe Recipe;
        internal int Batches;
    }

    internal sealed class WorkshopProductionMachine
    {
        internal TileEntityWorkstation Station;
        internal Recipe Recipe;
    }

    internal sealed class WorkshopProductionPlan
    {
        internal int Requested;
        internal int Maximum;
        internal string Missing = "";
        internal readonly Dictionary<int, HashSet<string>> Recipes = new Dictionary<int, HashSet<string>>();
        internal readonly Dictionary<string, WorkshopReservationClaim> Claims = new Dictionary<string, WorkshopReservationClaim>();
        internal readonly List<WorkshopProductionStep> Steps = new List<WorkshopProductionStep>();

        internal bool Allows(int type, Recipe recipe)
        {
            HashSet<string> keys;
            return !Recipes.TryGetValue(type, out keys) || keys.Count == 0 || keys.Contains(WorkshopManager.RecipeKey(recipe));
        }

        internal WorkshopReservationClaim Claim(Recipe recipe)
        {
            WorkshopReservationClaim claim;
            Claims.TryGetValue(WorkshopManager.RecipeKey(recipe), out claim);
            return claim;
        }
    }

    // Builds a complete bill of materials against a virtual inventory. Stored
    // items and already-pending outputs are separate: only real stored items
    // become withdrawal reservations. Binary search makes maximum-craftable
    // queries logarithmic even for the 100,000 item request limit.
    internal sealed class WorkshopProductionPlanner
    {
        internal sealed class Pool
        {
            internal readonly Dictionary<int, long> Current = new Dictionary<int, long>();
            internal readonly Dictionary<int, long> Future = new Dictionary<int, long>();
            internal readonly Dictionary<int, long> Units = new Dictionary<int, long>();

            internal Pool Clone()
            {
                var clone = new Pool();
                Copy(Current, clone.Current); Copy(Future, clone.Future); Copy(Units, clone.Units);
                return clone;
            }

            internal void ReplaceWith(Pool other)
            {
                Current.Clear(); Future.Clear(); Units.Clear();
                Copy(other.Current, Current); Copy(other.Future, Future); Copy(other.Units, Units);
            }

            private static void Copy(Dictionary<int, long> from, Dictionary<int, long> to)
            { foreach (var pair in from) to[pair.Key] = pair.Value; }

            internal long Take(int type, long count, out long currentTaken)
            {
                currentTaken = TakeFrom(Current, type, count);
                return currentTaken + TakeFrom(Future, type, count - currentTaken);
            }

            internal long TakeUnits(int type, long count) { return TakeFrom(Units, type, count); }
            internal void AddFuture(int type, long count) { Add(Future, type, count); }
            internal void AddUnits(int type, long count) { Add(Units, type, count); }
            internal void RemoveFuture(int type, long count) { TakeFrom(Future, type, count); }

            private static long TakeFrom(Dictionary<int, long> values, int type, long count)
            {
                if (count <= 0) return 0;
                long available;
                if (!values.TryGetValue(type, out available) || available <= 0) return 0;
                long used = Math.Min(available, count), left = available - used;
                if (left == 0) values.Remove(type); else values[type] = left;
                return used;
            }

            internal static void Add(Dictionary<int, long> values, int type, long count)
            {
                if (count <= 0) return;
                long current;
                values.TryGetValue(type, out current);
                values[type] = checked(current + count);
            }
        }

        private sealed class Option
        {
            internal Recipe Recipe;
            internal string Key;
            internal double SecondsPerOutput;
            internal int Machines;
        }

        private sealed class Attempt
        {
            internal readonly Dictionary<int, HashSet<string>> Recipes = new Dictionary<int, HashSet<string>>();
            internal readonly Dictionary<string, Dictionary<int, long>> Claims = new Dictionary<string, Dictionary<int, long>>();
            internal readonly Dictionary<int, long> Missing = new Dictionary<int, long>();
            internal readonly List<WorkshopProductionStep> Steps = new List<WorkshopProductionStep>();
            internal string Reason = "";

            internal Attempt Clone()
            {
                var clone = new Attempt { Reason = Reason };
                foreach (var pair in Recipes) clone.Recipes[pair.Key] = new HashSet<string>(pair.Value);
                foreach (var pair in Claims) clone.Claims[pair.Key] = new Dictionary<int, long>(pair.Value);
                foreach (var pair in Missing) clone.Missing[pair.Key] = pair.Value;
                clone.Steps.AddRange(Steps);
                return clone;
            }

            internal void ReplaceWith(Attempt other)
            {
                Recipes.Clear(); Claims.Clear(); Missing.Clear(); Reason = other.Reason;
                foreach (var pair in other.Recipes) Recipes[pair.Key] = new HashSet<string>(pair.Value);
                foreach (var pair in other.Claims) Claims[pair.Key] = new Dictionary<int, long>(pair.Value);
                foreach (var pair in other.Missing) Missing[pair.Key] = pair.Value;
                Steps.Clear(); Steps.AddRange(other.Steps);
            }

            internal void Use(int type, string key)
            {
                HashSet<string> keys;
                if (!Recipes.TryGetValue(type, out keys)) Recipes[type] = keys = new HashSet<string>();
                keys.Add(key);
            }

            internal void Claim(string key, int type, long count)
            {
                if (count <= 0) return;
                Dictionary<int, long> items;
                if (!Claims.TryGetValue(key, out items)) Claims[key] = items = new Dictionary<int, long>();
                long current;
                items.TryGetValue(type, out current);
                items[type] = checked(current + count);
            }

            internal void Need(int type, long count, string reason)
            {
                if (count > 0)
                {
                    long current;
                    Missing.TryGetValue(type, out current);
                    Missing[type] = checked(current + count);
                }
                if (string.IsNullOrEmpty(Reason)) Reason = reason;
            }

            internal long MissingTotal { get { return Missing.Values.Sum(); } }
        }

        private readonly StorageNetworkSession network;
        private readonly EntityPlayerLocal player;
        private readonly XUi xui;
        private readonly WorkshopControllerData controller;
        private readonly List<TileEntityWorkstation> stations;
        private readonly Dictionary<int, List<Option>> options = new Dictionary<int, List<Option>>();
        private readonly Dictionary<string, List<WorkshopProductionMachine>> machines = new Dictionary<string, List<WorkshopProductionMachine>>();
        private readonly bool allowDependencies;
        private readonly HashSet<int> requestedOutputs;
        private readonly Func<Recipe, TileEntityWorkstation, Recipe> prepare;

        internal WorkshopProductionPlanner(StorageNetworkSession network, EntityPlayerLocal player, XUi xui,
            WorkshopControllerData controller, List<TileEntityWorkstation> stations, bool allowDependencies,
            IEnumerable<int> requestedOutputs = null, Func<Recipe, TileEntityWorkstation, Recipe> prepare = null)
        {
            this.network = network; this.player = player; this.xui = xui; this.controller = controller;
            this.stations = stations; this.allowDependencies = allowDependencies;
            this.requestedOutputs = requestedOutputs == null ? null : new HashSet<int>(requestedOutputs);
            this.prepare = prepare;
            BuildOptions();
        }

        internal Pool CreatePool()
        {
            var pool = new Pool();
            foreach (var pair in network.SnapshotProductCounts(true)) pool.Current[pair.Key] = pair.Value;
            foreach (var output in WorkshopManager.OutstandingOutputs(stations))
                Pool.Add(pool.Future, output.itemValue.type, output.count);
            foreach (var job in controller.Smelting)
            {
                var item = ItemClass.GetItem(job.Item, false);
                if (item != null && !item.IsEmpty()) Pool.Add(pool.Future, item.type, job.Count);
            }
            var assigned = new HashSet<Vector3i>(controller.Smelting.Select(j => j.Position));
            var unitTypes = options.Values.SelectMany(list => list).Where(o => o.Recipe.materialBasedRecipe)
                .SelectMany(o => o.Recipe.ingredients).Select(i => i.itemValue.type).Distinct().ToList();
            foreach (int type in unitTypes)
            {
                var ingredient = new ItemStack(new ItemValue(type), 1);
                long units = stations.Where(s => !assigned.Contains(s.ToWorldPos()))
                    .Sum(s => WorkshopMachines.Uses(s, TileEntityWorkstation.Module.Material_Input)
                        ? WorkshopMachines.MaterialUnits(s, ingredient, true) : 0);
                if (units > 0) pool.Units[type] = units;
            }
            return pool;
        }

        internal IEnumerable<WorkshopProductionMachine> MachinesFor(Recipe recipe)
        { return MachinesFor(WorkshopManager.RecipeKey(recipe)); }

        internal IEnumerable<WorkshopProductionMachine> MachinesFor(string key)
        {
            List<WorkshopProductionMachine> result;
            return machines.TryGetValue(key, out result)
                ? result : Enumerable.Empty<WorkshopProductionMachine>();
        }

        internal WorkshopProductionPlan Plan(int type, int requested, Pool shared, WorkshopReservationBank bank)
        {
            var result = new WorkshopProductionPlan { Requested = Math.Max(0, requested) };
            if (requested <= 0) return result;
            Pool fullPool; Attempt fullAttempt;
            bool full = Try(type, requested, shared, out fullPool, out fullAttempt);
            Pool chosenPool = null; Attempt chosenAttempt = null;
            if (full)
            {
                result.Maximum = requested; chosenPool = fullPool; chosenAttempt = fullAttempt;
            }
            else
            {
                int low = 0, high = requested - 1;
                while (low < high)
                {
                    int amount = low + (high - low + 1) / 2;
                    Pool candidatePool; Attempt candidateAttempt;
                    if (Try(type, amount, shared, out candidatePool, out candidateAttempt)) low = amount;
                    else high = amount - 1;
                }
                result.Maximum = low;
                if (low > 0) Try(type, low, shared, out chosenPool, out chosenAttempt);
                result.Missing = DescribeMissing(fullAttempt);
            }
            if (chosenPool == null || chosenAttempt == null) return result;
            shared.ReplaceWith(chosenPool);
            result.Steps.AddRange(chosenAttempt.Steps);
            foreach (var pair in chosenAttempt.Recipes) result.Recipes[pair.Key] = new HashSet<string>(pair.Value);
            foreach (var pair in chosenAttempt.Claims)
            {
                var claim = new WorkshopReservationClaim(pair.Key);
                foreach (var item in pair.Value) claim.Add(item.Key, item.Value);
                result.Claims[pair.Key] = claim;
                if (bank != null) bank.Add(claim);
            }
            return result;
        }

        private bool Try(int type, int requested, Pool source, out Pool pool, out Attempt attempt)
        {
            pool = source.Clone(); attempt = new Attempt();
            if (requested <= 0) return true;
            int produced;
            if (!Produce(type, requested, pool, attempt, new HashSet<int>(), out produced)) return false;
            // A MAKE/stock deficit asks for newly produced root output; existing
            // stock and unrelated pending output must not satisfy it.
            pool.RemoveFuture(type, produced);
            return true;
        }

        private bool Produce(int type, long count, Pool pool, Attempt attempt, HashSet<int> path, out int produced)
        {
            produced = 0;
            if (count <= 0) return true;
            if (path.Count >= 8 || !path.Add(type))
            { attempt.Need(type, count, "Recipe dependency cycle or depth limit"); return false; }
            try
            {
                List<Option> list;
                if (!options.TryGetValue(type, out list) || list.Count == 0)
                {
                    attempt.Need(type, count, "No enabled machine has a productive unlocked recipe");
                    return false;
                }
                Attempt bestFailure = null;
                foreach (var option in list.OrderBy(o => AvailabilityRank(o.Recipe, count, pool)).ThenBy(o =>
                    Math.Ceiling(count / (double)o.Recipe.count) * o.Recipe.craftingTime / Math.Max(1, o.Machines)))
                {
                    var candidatePool = pool.Clone(); var candidateAttempt = attempt.Clone();
                    long batchesLong = (count + option.Recipe.count - 1) / option.Recipe.count;
                    if (batchesLong <= 0 || batchesLong > WorkshopRules.MaximumTarget)
                    { candidateAttempt.Need(type, count, "Recipe batch is outside the supported range"); continue; }
                    int batches = (int)batchesLong;
                    bool ok = true;
                    foreach (var ingredient in option.Recipe.ingredients)
                    {
                        long required = checked((long)ingredient.count * batches);
                        if (required <= 0) continue;
                        if (option.Recipe.materialBasedRecipe)
                            ok = PlanMaterial(option, ingredient, required, candidatePool, candidateAttempt, path);
                        else ok = PlanIngredient(option.Key, ingredient.itemValue.type, required, candidatePool, candidateAttempt, path);
                        if (!ok) break;
                    }
                    if (!ok)
                    {
                        if (bestFailure == null || candidateAttempt.MissingTotal < bestFailure.MissingTotal) bestFailure = candidateAttempt;
                        continue;
                    }
                    produced = checked(option.Recipe.count * batches);
                    candidatePool.AddFuture(type, produced);
                    candidateAttempt.Use(type, option.Key);
                    candidateAttempt.Steps.Add(new WorkshopProductionStep { Recipe = option.Recipe, Batches = batches });
                    pool.ReplaceWith(candidatePool); attempt.ReplaceWith(candidateAttempt);
                    return true;
                }
                if (bestFailure != null) attempt.ReplaceWith(bestFailure);
                return false;
            }
            finally { path.Remove(type); }
        }

        private bool PlanIngredient(string ownerKey, int type, long required, Pool pool, Attempt attempt, HashSet<int> path)
        {
            long current;
            long used = pool.Take(type, required, out current);
            attempt.Claim(ownerKey, type, current);
            long missing = required - used;
            if (missing <= 0) return true;
            if (!allowDependencies)
            {
                attempt.Need(type, missing, "Craft missing ingredients is off");
                return false;
            }
            int produced;
            if (!Produce(type, missing, pool, attempt, path, out produced)) return false;
            long generatedCurrent;
            long generated = pool.Take(type, missing, out generatedCurrent);
            if (generated < missing)
            {
                attempt.Need(type, missing - generated, "Planned ingredient output was insufficient");
                return false;
            }
            return true;
        }

        private bool PlanMaterial(Option option, ItemStack ingredient, long required, Pool pool,
            Attempt attempt, HashSet<int> path)
        {
            long units = pool.TakeUnits(ingredient.itemValue.type, required);
            long missingUnits = required - units;
            if (missingUnits <= 0) return true;
            string rawName = WorkshopMachines.RawMaterial(ingredient.itemValue.ItemClass.GetItemName());
            var raw = string.IsNullOrEmpty(rawName) ? null : ItemClass.GetItem(rawName, false);
            if (raw == null || raw.IsEmpty() || raw.type == option.Recipe.itemValueType)
            {
                attempt.Need(option.Recipe.itemValueType, option.Recipe.count,
                    raw != null && raw.type == option.Recipe.itemValueType ? "Rejected self-consuming forge recipe" : "Supply this forge material manually");
                return false;
            }
            int weight = raw.ItemClass.GetWeight();
            if (weight <= 0) { attempt.Need(raw.type, 1, "Forge material has no usable weight"); return false; }
            long rawCount = (missingUnits + weight - 1) / weight;
            if (!PlanIngredient(option.Key, raw.type, rawCount, pool, attempt, path)) return false;
            pool.AddUnits(ingredient.itemValue.type, rawCount * weight - missingUnits);
            return true;
        }

        private static int AvailabilityRank(Recipe recipe, long count, Pool pool)
        {
            long batches = (count + recipe.count - 1) / recipe.count;
            bool current = true, pending = true;
            foreach (var ingredient in recipe.ingredients)
            {
                long stored, future;
                var source = recipe.materialBasedRecipe ? pool.Units : pool.Current;
                source.TryGetValue(ingredient.itemValue.type, out stored);
                pool.Future.TryGetValue(ingredient.itemValue.type, out future);
                long needed = (long)ingredient.count * batches;
                current &= stored >= needed;
                pending &= stored + (recipe.materialBasedRecipe ? 0 : future) >= needed;
            }
            return current ? 0 : pending ? 1 : 2;
        }

        private void BuildOptions()
        {
            // Workstation recipe lists are cheap to enumerate. Preparing every
            // recipe applies player effects and allocates staged recipes even
            // though a controller may request only one product. Follow its
            // ingredient graph first, then stage only reachable outputs.
            var byStation = new Dictionary<string, List<Recipe>>();
            var byOutput = requestedOutputs == null ? null : new Dictionary<int, List<Recipe>>();
            foreach (var station in stations)
            {
                if (station == null || station.IsUserAccessing()) continue;
                string name = station.block.GetBlockName();
                if (byStation.ContainsKey(name)) continue;
                List<Recipe> recipesForStation = XUiM_Recipes.FilterRecipesByWorkstation(name,
                    XUiM_Recipes.GetRecipes()).ToList();
                byStation.Add(name, recipesForStation);
                if (byOutput == null) continue;
                foreach (var recipe in recipesForStation)
                {
                    List<Recipe> outputs;
                    if (!byOutput.TryGetValue(recipe.itemValueType, out outputs))
                        byOutput.Add(recipe.itemValueType, outputs = new List<Recipe>());
                    outputs.Add(recipe);
                }
            }
            HashSet<int> needed = null;
            if (requestedOutputs != null)
            {
                needed = new HashSet<int>(requestedOutputs);
                var pending = new Queue<int>(needed);
                while (pending.Count > 0)
                {
                    List<Recipe> producing;
                    if (!byOutput.TryGetValue(pending.Dequeue(), out producing)) continue;
                    foreach (var recipe in producing)
                    {
                        if (!WorkshopManager.Productive(recipe)) continue;
                        foreach (var ingredient in recipe.ingredients)
                        {
                            if (ingredient == null || ingredient.count <= 0) continue;
                            int type = ingredient.itemValue.type;
                            if (recipe.materialBasedRecipe)
                            {
                                string rawName = WorkshopMachines.RawMaterial(ingredient.itemValue.ItemClass.GetItemName());
                                var raw = string.IsNullOrEmpty(rawName) ? null : ItemClass.GetItem(rawName, false);
                                if (raw != null && !raw.IsEmpty()) type = raw.type;
                            }
                            if (needed.Add(type)) pending.Enqueue(type);
                        }
                    }
                }
            }
            var grouped = new Dictionary<string, List<Option>>();
            foreach (var station in stations)
            {
                if (station == null || station.IsUserAccessing()) continue;
                foreach (var recipe in byStation[station.block.GetBlockName()])
                {
                    if (needed != null && !needed.Contains(recipe.itemValueType)) continue;
                    string reason;
                    if (!WorkshopManager.RecipeUsable(recipe, station, player, out reason)) continue;
                    var staged = prepare == null ? WorkshopManager.PrepareRecipe(recipe, xui, station) : prepare(recipe, station);
                    if (!WorkshopManager.Productive(staged) || staged.count <= 0) continue;
                    string key = WorkshopManager.RecipeKey(staged);
                    List<WorkshopProductionMachine> matching;
                    if (!machines.TryGetValue(key, out matching)) machines[key] = matching = new List<WorkshopProductionMachine>();
                    var existingMachine = matching.Find(m => m.Station == station);
                    if (existingMachine == null) matching.Add(new WorkshopProductionMachine { Station = station, Recipe = staged });
                    else if (staged.craftingTime < existingMachine.Recipe.craftingTime) existingMachine.Recipe = staged;
                    List<Option> same;
                    if (!grouped.TryGetValue(key, out same)) grouped[key] = same = new List<Option>();
                    same.Add(new Option { Recipe = staged, Key = key,
                        SecondsPerOutput = Math.Max(.05, staged.craftingTime) / staged.count, Machines = 1 });
                }
            }
            foreach (var group in grouped.Values)
            {
                var fastest = group.OrderBy(o => o.SecondsPerOutput).First();
                fastest.Machines = group.Count;
                List<Option> list;
                if (!options.TryGetValue(fastest.Recipe.itemValueType, out list))
                    options[fastest.Recipe.itemValueType] = list = new List<Option>();
                list.Add(fastest);
            }
            foreach (var list in options.Values)
                list.Sort((left, right) => (left.SecondsPerOutput / Math.Max(1, left.Machines))
                    .CompareTo(right.SecondsPerOutput / Math.Max(1, right.Machines)));
        }

        private static string DescribeMissing(Attempt attempt)
        {
            if (attempt == null) return "Unable to build a production plan";
            if (attempt.Missing.Count == 0) return string.IsNullOrEmpty(attempt.Reason) ? "Insufficient supplies" : attempt.Reason;
            return string.Join(", ", attempt.Missing.OrderByDescending(p => p.Value).Take(3).Select(pair =>
            {
                var item = ItemClass.GetForId(pair.Key);
                string name = item == null ? "item #" + pair.Key : Localization.Get(item.GetItemName());
                if (string.IsNullOrEmpty(name) && item != null) name = item.GetItemName();
                return pair.Value + " " + name;
            }));
        }
    }
}
