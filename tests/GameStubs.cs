// Deliberately minimal model: tests exercise the production planner, not Unity,
// native cursor handling, networking or the game's ItemValue implementation.
public sealed class PackedBoolArray
{
    private readonly bool[] values;
    public PackedBoolArray(int length) { values = new bool[length]; }
    public int Length => values.Length;
    public bool this[int index] { get => values[index]; set => values[index] = value; }
}

public sealed partial class ItemClass
{
    public int MaxCount = 100;
    public bool CanStore = true;
    public bool CanPlaceInContainer() => CanStore;
    public bool CanStack() => MaxCount > 1;
}

public sealed partial class ItemValue
{
    public int type;
    public int Metadata;
    public int Quality;
    public ushort Seed;
    public byte Flags;
    public int TextureFullArray;
    public bool HasQuality;
    public bool HasModSlots;
    public bool InstalledMods;
    public ItemClass ItemClassOrMissing = new ItemClass();
    public static ItemValue None => new ItemValue();
    public ItemValue Clone() => new ItemValue { type = type, Metadata = Metadata, Quality = Quality, Seed = Seed,
        Flags = Flags, TextureFullArray = TextureFullArray, HasQuality = HasQuality,
        HasModSlots = HasModSlots, InstalledMods = InstalledMods,
        ItemClassOrMissing = ItemClassOrMissing };
    public bool HasMods() => InstalledMods;
    public override bool Equals(object obj) => obj is ItemValue other && type == other.type
        && Metadata == other.Metadata && Quality == other.Quality && Seed == other.Seed && HasQuality == other.HasQuality;
    public override int GetHashCode() => HashCode.Combine(type, Metadata, Quality);
}

// Marker for 3.3 grid ownership: GameSlots must overwrite bound slots in place.
public sealed class ItemStackGrid { }

public sealed partial class ItemStack
{
    public ItemValue itemValue;
    public int count;
    public ItemStackGrid owner;
    public void Set(ItemValue value, int amount) { itemValue = value; count = amount; }
    public static ItemStack Empty => new ItemStack(new ItemValue(), 0);
    public ItemStack(ItemValue value, int amount) { itemValue = value; count = amount; }
    public bool IsEmpty() => count <= 0 || itemValue.type == 0;
    public ItemStack Clone() => new ItemStack(itemValue.Clone(), count);
    public bool CanStackWith(ItemStack other, bool partial) => itemValue.type == other.itemValue.type
        && itemValue.TextureFullArray == other.itemValue.TextureFullArray
        && itemValue.ItemClassOrMissing.MaxCount > count;
    public static ItemStack[] Clone(ItemStack[] source) => source.Select(s => s?.Clone()).ToArray();
}
