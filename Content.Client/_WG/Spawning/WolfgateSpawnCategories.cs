using System.Linq;
using System.Text;
using Content.Client._WG.Stylesheets;

namespace Content.Client._WG.Spawning;

/// <summary>
/// Top-level folders of the spawn menu, listed in the sidebar in declaration order.
/// </summary>
public enum WgSpawnGroup : byte
{
    Mobs,
    Weapons,
    Vehicles,
    Streets,
    Xenonids,
    Clothing,
    Engineering,
    Medical,
    FoodDrink,
    Machines,
    Structures,
    Devices,
    Fun,
    Science,
    Effects,
    Mapping,
    Misc,
}

/// <summary>
/// Leaf categories. Every spawnable prototype is classified into exactly one of these, so the counts in the
/// sidebar add up to the total. Declaration order is sidebar order within a group.
/// </summary>
public enum WgSpawnCategory : byte
{
    // Mobs
    Humanoids,
    Silicons,
    Animals,
    MobsOther,

    // Weapons
    MeleeWeapons,
    Guns,
    Ammunition,
    Explosives,
    Shields,

    // Vehicles
    VehiclesDrivable,
    VehicleParts,
    VehicleInteriors,
    VehicleScenery,

    // Streets
    StreetFurniture,
    StreetSigns,
    StreetLighting,
    StreetBuildings,
    StreetYards,
    // Xenonids
    XenoCastes,
    XenoHive,

    // Clothing
    ClothingHead,
    ClothingEyes,
    ClothingNeck,
    ClothingOuter,
    ClothingUniform,
    ClothingHands,
    ClothingShoes,
    ClothingBelt,
    ClothingBack,
    ClothingOther,

    // Engineering
    Tools,
    Materials,
    Parts,
    Power,
    Atmospherics,

    // Medical
    Medicine,
    Chemistry,
    BodyParts,

    // Food & drink
    Food,
    Drinks,
    Botany,

    // Machines
    Computers,
    Machines,
    Vending,
    Disposals,

    // Structures
    WallsWindows,
    Doors,
    Furniture,
    Lockers,
    Lights,
    Decoration,
    Signs,

    // Devices
    Identification,
    Communications,
    Instruments,
    DevicesOther,

    // Fun
    Toys,
    Games,
    Documents,

    // Science
    Anomalies,
    Research,

    // Effects
    Projectiles,
    Effects,

    // Mapping
    Markers,
    Spawners,
    MappingTools,
    DoNotMap,
    Debug,

    // Misc
    ItemsOther,
    StructuresOther,
    Uncategorised,
}

/// <summary>
/// Static description of the category tree: which group a leaf hangs under, what it is called and what colour
/// its group reads as.
/// </summary>
public static class WgSpawnCategories
{
    private static readonly (WgSpawnCategory Category, WgSpawnGroup Group)[] Table =
    {
        (WgSpawnCategory.Humanoids, WgSpawnGroup.Mobs),
        (WgSpawnCategory.Silicons, WgSpawnGroup.Mobs),
        (WgSpawnCategory.Animals, WgSpawnGroup.Mobs),
        (WgSpawnCategory.MobsOther, WgSpawnGroup.Mobs),

        (WgSpawnCategory.MeleeWeapons, WgSpawnGroup.Weapons),
        (WgSpawnCategory.Guns, WgSpawnGroup.Weapons),
        (WgSpawnCategory.Ammunition, WgSpawnGroup.Weapons),
        (WgSpawnCategory.Explosives, WgSpawnGroup.Weapons),
        (WgSpawnCategory.Shields, WgSpawnGroup.Weapons),

        (WgSpawnCategory.VehiclesDrivable, WgSpawnGroup.Vehicles),
        (WgSpawnCategory.VehicleParts, WgSpawnGroup.Vehicles),
        (WgSpawnCategory.VehicleInteriors, WgSpawnGroup.Vehicles),
        (WgSpawnCategory.VehicleScenery, WgSpawnGroup.Vehicles),

        (WgSpawnCategory.StreetFurniture, WgSpawnGroup.Streets),
        (WgSpawnCategory.StreetSigns, WgSpawnGroup.Streets),
        (WgSpawnCategory.StreetLighting, WgSpawnGroup.Streets),
        (WgSpawnCategory.StreetBuildings, WgSpawnGroup.Streets),
        (WgSpawnCategory.StreetYards, WgSpawnGroup.Streets),
        (WgSpawnCategory.XenoCastes, WgSpawnGroup.Xenonids),
        (WgSpawnCategory.XenoHive, WgSpawnGroup.Xenonids),

        (WgSpawnCategory.ClothingHead, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingEyes, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingNeck, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingOuter, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingUniform, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingHands, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingShoes, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingBelt, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingBack, WgSpawnGroup.Clothing),
        (WgSpawnCategory.ClothingOther, WgSpawnGroup.Clothing),

        (WgSpawnCategory.Tools, WgSpawnGroup.Engineering),
        (WgSpawnCategory.Materials, WgSpawnGroup.Engineering),
        (WgSpawnCategory.Parts, WgSpawnGroup.Engineering),
        (WgSpawnCategory.Power, WgSpawnGroup.Engineering),
        (WgSpawnCategory.Atmospherics, WgSpawnGroup.Engineering),

        (WgSpawnCategory.Medicine, WgSpawnGroup.Medical),
        (WgSpawnCategory.Chemistry, WgSpawnGroup.Medical),
        (WgSpawnCategory.BodyParts, WgSpawnGroup.Medical),

        (WgSpawnCategory.Food, WgSpawnGroup.FoodDrink),
        (WgSpawnCategory.Drinks, WgSpawnGroup.FoodDrink),
        (WgSpawnCategory.Botany, WgSpawnGroup.FoodDrink),

        (WgSpawnCategory.Computers, WgSpawnGroup.Machines),
        (WgSpawnCategory.Machines, WgSpawnGroup.Machines),
        (WgSpawnCategory.Vending, WgSpawnGroup.Machines),
        (WgSpawnCategory.Disposals, WgSpawnGroup.Machines),

        (WgSpawnCategory.WallsWindows, WgSpawnGroup.Structures),
        (WgSpawnCategory.Doors, WgSpawnGroup.Structures),
        (WgSpawnCategory.Furniture, WgSpawnGroup.Structures),
        (WgSpawnCategory.Lockers, WgSpawnGroup.Structures),
        (WgSpawnCategory.Lights, WgSpawnGroup.Structures),
        (WgSpawnCategory.Decoration, WgSpawnGroup.Structures),
        (WgSpawnCategory.Signs, WgSpawnGroup.Structures),

        (WgSpawnCategory.Identification, WgSpawnGroup.Devices),
        (WgSpawnCategory.Communications, WgSpawnGroup.Devices),
        (WgSpawnCategory.Instruments, WgSpawnGroup.Devices),
        (WgSpawnCategory.DevicesOther, WgSpawnGroup.Devices),

        (WgSpawnCategory.Toys, WgSpawnGroup.Fun),
        (WgSpawnCategory.Games, WgSpawnGroup.Fun),
        (WgSpawnCategory.Documents, WgSpawnGroup.Fun),

        (WgSpawnCategory.Anomalies, WgSpawnGroup.Science),
        (WgSpawnCategory.Research, WgSpawnGroup.Science),

        (WgSpawnCategory.Projectiles, WgSpawnGroup.Effects),
        (WgSpawnCategory.Effects, WgSpawnGroup.Effects),

        (WgSpawnCategory.Markers, WgSpawnGroup.Mapping),
        (WgSpawnCategory.Spawners, WgSpawnGroup.Mapping),
        (WgSpawnCategory.MappingTools, WgSpawnGroup.Mapping),
        (WgSpawnCategory.DoNotMap, WgSpawnGroup.Mapping),
        (WgSpawnCategory.Debug, WgSpawnGroup.Mapping),

        (WgSpawnCategory.ItemsOther, WgSpawnGroup.Misc),
        (WgSpawnCategory.StructuresOther, WgSpawnGroup.Misc),
        (WgSpawnCategory.Uncategorised, WgSpawnGroup.Misc),
    };

    private static readonly WgSpawnGroup[] GroupOf = BuildGroupLookup();

    /// <summary>Groups in sidebar order, each with its leaves in sidebar order.</summary>
    public static readonly (WgSpawnGroup Group, WgSpawnCategory[] Categories)[] Tree =
        Table.GroupBy(e => e.Group)
            .OrderBy(g => (int) g.Key)
            .Select(g => (g.Key, g.Select(e => e.Category).ToArray()))
            .ToArray();

    /// <summary>The sidebar group a category sits under.</summary>
    public static WgSpawnGroup GroupFor(WgSpawnCategory category) => GroupOf[(int) category];

    /// <summary>Localized category name.</summary>
    public static string CategoryName(WgSpawnCategory category) =>
        Loc.GetString($"wg-spawn-category-{Kebab(category.ToString())}");

    /// <summary>Localized group name.</summary>
    public static string GroupName(WgSpawnGroup group) =>
        Loc.GetString($"wg-spawn-group-{Kebab(group.ToString())}");

    /// <summary>
    /// Each group reads as one colour across the sidebar stripe and the tile stripe, so a glance at the grid
    /// says what kind of thing you are looking at.
    /// </summary>
    public static Color GroupColor(WgSpawnGroup group, WolfgateSkin skin)
    {
        return group switch
        {
            WgSpawnGroup.Mobs => Color.FromHex("#6FD98A"),
            WgSpawnGroup.Weapons => skin.Danger,
            WgSpawnGroup.Vehicles => Color.FromHex("#8FA86B"),
            WgSpawnGroup.Streets => Color.FromHex("#C9B48A"),
            WgSpawnGroup.Xenonids => Color.FromHex("#A2D94B"),
            WgSpawnGroup.Clothing => Color.FromHex("#C79BFF"),
            WgSpawnGroup.Engineering => Color.FromHex("#FFC14D"),
            WgSpawnGroup.Medical => Color.FromHex("#7FC7FF"),
            WgSpawnGroup.FoodDrink => Color.FromHex("#FF9E6B"),
            WgSpawnGroup.Machines => Color.FromHex("#9FB4C7"),
            WgSpawnGroup.Structures => Color.FromHex("#B08A67"),
            WgSpawnGroup.Devices => skin.Accent,
            WgSpawnGroup.Fun => Color.FromHex("#FF8ED0"),
            WgSpawnGroup.Science => Color.FromHex("#B36BFF"),
            WgSpawnGroup.Effects => Color.FromHex("#7FE6E6"),
            WgSpawnGroup.Mapping => skin.Caution,
            _ => skin.TextMuted,
        };
    }

    private static WgSpawnGroup[] BuildGroupLookup()
    {
        var lookup = new WgSpawnGroup[Enum.GetValues<WgSpawnCategory>().Length];
        foreach (var (category, group) in Table)
        {
            lookup[(int) category] = group;
        }

        return lookup;
    }

    /// <summary>PascalCase enum name to the kebab-case tail of its locale id.</summary>
    private static string Kebab(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
