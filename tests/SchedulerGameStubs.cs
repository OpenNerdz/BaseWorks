public partial class TileEntityWorkstation { public float CraftSpeed = 1; }
public partial class Recipe
{
    public string craftingArea;
    public int craftingToolType;
    public float craftingTime = 10;
    public bool Unlocked = true;
    public bool IsUnlocked(EntityPlayerLocal _) => Unlocked;
}
public class EntityPlayerLocal { public int entityId = 1; }
public class XUi { }
public class GameManager { public static GameManager Instance = new(); public World World = new(); }
public partial class World { public EntityPlayerLocal GetPrimaryPlayer() => new(); }
public static partial class XUiM_Recipes
{
    public static List<Recipe> All = new();
    public static List<Recipe> GetRecipes() => All;
    public static List<Recipe> FilterRecipesByWorkstation(string name, List<Recipe> recipes) => recipes.Where(r => r.craftingArea == name).ToList();
}
public enum EnumGamePrefs { GameWorld, GameName }
public static class GamePrefs
{
    public static string SaveName = "TestSave";
    public static string GetString(EnumGamePrefs pref) => pref == EnumGamePrefs.GameWorld ? "TestWorld" : SaveName;
}
namespace NearbyCraft
{
    internal static partial class WorkshopManager
    {
        internal static int PreparedRecipes;
        internal static Dictionary<Vector3i, string> Status = new(), StationStatus = new();
        internal static Dictionary<string, string> TargetStatus = new();
        internal static string TargetKey(Vector3i pos, string item) => pos + "/" + item;
        internal static bool Supported(Recipe r) => r != null;
        internal static bool Productive(Recipe r)
        {
            if (r == null) return false;
            foreach (var ingredient in r.ingredients)
            {
                int type = ingredient.itemValue.type;
                if (r.materialBasedRecipe)
                {
                    string rawName = WorkshopMachines.RawMaterial(ingredient.itemValue.ItemClass.GetItemName());
                    var raw = string.IsNullOrEmpty(rawName) ? null : ItemClass.GetItem(rawName, false);
                    if (raw != null && !raw.IsEmpty()) type = raw.type;
                }
                if (type == r.itemValueType) return false;
            }
            return true;
        }
        internal static string RecipeKey(Recipe r) => r.GetName() + "/" + r.craftingArea + "/" + r.count + "/" + r.craftingToolType + "/"
            + string.Join(";", r.ingredients.Select(i => i.itemValue.ItemClass.GetItemName() + ":" + i.count));
        internal static bool RecipeUsable(Recipe r, TileEntityWorkstation station, EntityPlayerLocal player, out string reason)
        {
            reason = "";
            if (!Productive(r)) { reason = "Recipe consumes its own output"; return false; }
            if (!r.IsUnlocked(player)) { reason = "Unlock this recipe first"; return false; }
            if (r.craftingToolType != 0 && !station.Tools.Any(t => !t.IsEmpty() && t.itemValue.type == r.craftingToolType))
            { reason = "Install tool"; return false; }
            if (station.IsBesideWater) { reason = "Move the machine away from water"; return false; }
            return true;
        }
        internal static Recipe PrepareRecipe(Recipe r, XUi _, TileEntityWorkstation station = null) { PreparedRecipes++; return new Recipe {
            itemValueType = r.itemValueType, count = r.count, craftingArea = r.craftingArea,
            craftingTime = r.craftingTime * (station?.CraftSpeed ?? 1), craftingToolType = r.craftingToolType,
            craftingTier = r.craftingTier, materialBasedRecipe = r.materialBasedRecipe, Unlocked = r.Unlocked, ingredients = r.ingredients
        }; }
        internal static long CountQueued(TileEntityWorkstation station, int type) => station.Queue.Where(q => q?.Recipe != null && q.Multiplier > 0 && q.Recipe.itemValueType == type).Sum(q => (long)q.Multiplier * q.Recipe.count);
        internal static long CountOutput(TileEntityWorkstation station, int type) => station.Output.Where(s => s != null && !s.IsEmpty() && s.itemValue.type == type).Sum(s => (long)s.count);
        internal static List<ItemStack> OutstandingOutputs(List<TileEntityWorkstation> stations) => stations.SelectMany(s => s.Output.Where(i => i != null && !i.IsEmpty()).Select(i => i.Clone())
            .Concat(s.Queue.Where(q => q?.Recipe != null && q.Multiplier > 0).Select(q => new ItemStack(new ItemValue(q.Recipe.itemValueType), q.Recipe.count * q.Multiplier)))).ToList();
    }
}
