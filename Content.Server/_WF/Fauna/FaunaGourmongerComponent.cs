using Content.Shared._WF.Fauna;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A mob that banks the food it eats, radiates by how much it holds, turns hostile when starving and splits in two
/// after enough meals. Needs <see cref="FaunaCorpseEaterComponent"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaGourmongerComponent : Component
{
    /// <summary>
    /// Banked food.
    /// </summary>
    [DataField]
    public float Kcal = 200f;

    /// <summary>
    /// Below this it turns hostile.
    /// </summary>
    [DataField]
    public float StarvingKcal = 25f;

    /// <summary>
    /// Below this its stomach grumbles.
    /// </summary>
    [DataField]
    public float HungryKcal = 150f;

    /// <summary>
    /// Above this a hostile one calms down.
    /// </summary>
    [DataField]
    public float SatisfiedKcal = 500f;

    /// <summary>
    /// Above this examining says it's fat.
    /// </summary>
    [DataField]
    public float FatKcal = 1000f;

    /// <summary>
    /// Food from a food item, per unit of its solution.
    /// </summary>
    [DataField]
    public float KcalPerFoodUnit = 7.5f;

    /// <summary>
    /// Food from a corpse, per point of damage the corpse could take before dying.
    /// </summary>
    [DataField]
    public float KcalPerCorpseHealth = 2f;

    /// <summary>
    /// Meals needed to split, raised by the number alive when it spawns.
    /// </summary>
    [DataField]
    public int GrowToSplit = 20;

    /// <summary>
    /// It won't split while this many gourmongers are alive.
    /// </summary>
    [DataField]
    public int MaxAlive = 30;

    /// <summary>
    /// Offspring spawned when it splits.
    /// </summary>
    [DataField]
    public int SplitInto = 2;

    /// <summary>
    /// Share of its banked food each offspring gets on top of its own.
    /// </summary>
    [DataField]
    public float OffspringKcalShare = 0.1f;

    /// <summary>
    /// Meal count and max health gained from a food item and from a corpse.
    /// </summary>
    [DataField]
    public int FoodGrowth = 1;

    /// <summary>
    /// Meal count and max health gained from a corpse.
    /// </summary>
    [DataField]
    public int CorpseGrowth = 2;

    /// <summary>
    /// Radiation per second per banked kcal.
    /// </summary>
    [DataField]
    public float RadiationPerKcal = 1f / 150f;

    /// <summary>
    /// Tiles the radiation reaches.
    /// </summary>
    [DataField]
    public float RadiationRange = 3f;

    /// <summary>
    /// Faction while it is fed.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype> CalmFaction = "Passive";

    /// <summary>
    /// Faction while it is starving.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype> HangryFaction = "SimpleHostile";

    /// <summary>
    /// Meat dropped on death, holding its banked food.
    /// </summary>
    [DataField]
    public EntProtoId Meat = "FoodMeat";

    /// <summary>
    /// Reagent added to the dropped meat.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype> Nutriment = "Nutriment";

    /// <summary>
    /// Banked kcal per unit of nutriment in the dropped meat.
    /// </summary>
    [DataField]
    public float KcalPerNutriment = 15f;

    /// <summary>
    /// Most nutriment one piece of dropped meat holds.
    /// </summary>
    [DataField]
    public float NutrimentPerMeat = 20f;

    /// <summary>
    /// Spawned at a chance of <see cref="HeartChance"/> when a player hand-feeds it.
    /// </summary>
    [DataField]
    public EntProtoId? Heart;

    /// <summary>
    /// Chance from 0 to 1 that a hand-feed spawns <see cref="Heart"/>.
    /// </summary>
    [DataField]
    public float HeartChance = 0.05f;

    /// <summary>
    /// Chance from 0 to 1 per tick that a hungry one grumbles.
    /// </summary>
    [DataField]
    public float GrumbleChance = 0.1f;

    /// <summary>
    /// Popups picked from when it grumbles.
    /// </summary>
    [DataField]
    public List<LocId> Grumbles = new()
    {
        "fauna-gourmonger-grumble-1",
        "fauna-gourmonger-grumble-2",
        "fauna-gourmonger-grumble-3",
    };

    /// <summary>
    /// Shown when a starving one sniffs for food.
    /// </summary>
    [DataField]
    public LocId SniffPopup = "fauna-gourmonger-sniff";

    /// <summary>
    /// Shown to a player who tries to feed it while it is eating.
    /// </summary>
    [DataField]
    public LocId BusyPopup = "fauna-gourmonger-busy";

    /// <summary>
    /// Examine text below <see cref="HungryKcal"/>.
    /// </summary>
    [DataField]
    public LocId FamishedExamine = "fauna-gourmonger-examine-famished";

    /// <summary>
    /// Examine text above <see cref="FatKcal"/>.
    /// </summary>
    [DataField]
    public LocId FatExamine = "fauna-gourmonger-examine-fat";

    /// <summary>
    /// Time between hunger ticks.
    /// </summary>
    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Time between sniffs while starving.
    /// </summary>
    [DataField]
    public TimeSpan SniffInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the sniffing sprite state shows.
    /// </summary>
    [DataField]
    public TimeSpan SniffDuration = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Light colour once it has a quarter of the meals it needs to split.
    /// </summary>
    [DataField]
    public Color GlowColor = Color.FromHex("#5ad35a");

    /// <summary>
    /// Whether it is starving and hostile.
    /// </summary>
    [ViewVariables]
    public bool Hangry;

    /// <summary>
    /// Meals eaten toward <see cref="GrowToSplit"/>.
    /// </summary>
    [ViewVariables]
    public int Meals;

    /// <summary>
    /// Ticks since it last lost a meal. Raises food loss and the chance to lose a meal.
    /// </summary>
    [ViewVariables]
    public int FastingTicks;

    /// <summary>
    /// When the next hunger tick runs.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextTick;

    /// <summary>
    /// When it may sniff again.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextSniff;

    /// <summary>
    /// When the sniffing sprite state ends.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan SniffEnd;

    /// <summary>
    /// Last state written to appearance.
    /// </summary>
    [ViewVariables]
    public FaunaGourmongerState? VisualState;
}
