using Content.Server._WG.Farming;
using Content.Shared._WG.Farming;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.Hunting;

/// <summary>A game animal: the prized cut and offal a clean kill yields on top of its butchering.</summary>
[RegisterComponent, Access(typeof(WGHuntingSystem))]
public sealed partial class WGGameComponent : Component
{
    /// <summary>The best cut, given only for a clean kill.</summary>
    [DataField]
    public EntProtoId? PrizedCut;

    /// <summary>Offal it gives, each on its own chance, fewer from a messy kill.</summary>
    [DataField]
    public List<EntProtoId> Offal = new();

    /// <summary>Total damage when it died, so later harm to the carcass doesn't change the grade; null while alive.</summary>
    [DataField]
    public float? DeathDamage;
}

/// <summary>Grades butchered game by how clean the kill was.</summary>
public sealed class WGHuntingSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> GameMeat = "WGGameMeat";

    /// <summary>Damage past the killing blow, as a share of it, below which a kill is clean, and above which it is messy.</summary>
    private const float Clean = 1.25f;
    private const float Messy = 2f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGButcheredEvent>(OnButchered);
        SubscribeLocalEvent<WGGameComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<WGGameComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            ent.Comp.DeathDamage = CompOrNull<DamageableComponent>(ent)?.TotalDamage.Float();
        else if (args.NewMobState == MobState.Alive)
            ent.Comp.DeathDamage = null;
    }

    /// <summary>How clean the kill was, by its damage when it died: 0 clean, 1 fair, 2 messy.</summary>
    public int KillGrade(EntityUid carcass)
    {
        var total = CompOrNull<WGGameComponent>(carcass)?.DeathDamage ?? CompOrNull<DamageableComponent>(carcass)?.TotalDamage.Float();
        if (total == null || !_thresholds.TryGetThresholdForState(carcass, MobState.Dead, out var dead) || dead.Value <= 0)
            return 1;

        var ratio = total.Value / dead.Value.Float();
        return ratio <= Clean ? 0 : ratio >= Messy ? 2 : 1;
    }

    private void OnButchered(ref WGButcheredEvent args)
    {
        if (!TryComp<WGGameComponent>(args.Carcass, out var game))
            return;

        var grade = KillGrade(args.Carcass);
        var where = Transform(args.Carcass).Coordinates;
        var meats = args.Spawned.FindAll(e => _tags.HasTag(e, GameMeat));

        foreach (var meat in meats)
        {
            var quality = grade switch
            {
                0 => _random.Prob(0.35f) ? WGCropQuality.Gold : WGCropQuality.Silver,
                1 => _random.Prob(0.3f) ? WGCropQuality.Silver : WGCropQuality.Normal,
                _ => WGCropQuality.Normal,
            };
            if (quality != WGCropQuality.Normal)
                EnsureComp<WGProduceQualityComponent>(meat).Quality = quality;
        }

        // A messy kill spoils a cut or two.
        if (grade == 2)
        {
            for (var i = 0; i < Math.Min(2, meats.Count - 1); i++)
                QueueDel(meats[i]);
        }

        if (grade == 0 && game.PrizedCut is { } prized)
        {
            var cut = Spawn(prized, where);
            EnsureComp<WGProduceQualityComponent>(cut).Quality = WGCropQuality.Gold;
        }

        foreach (var offal in game.Offal)
        {
            if (_random.Prob(grade switch { 0 => 1f, 1 => 0.6f, _ => 0.2f }))
                Spawn(offal, where);
        }

        _popup.PopupEntity(Loc.GetString("wg-hunt-kill-" + grade switch { 0 => "clean", 1 => "fair", _ => "messy" }), args.User, args.User);
    }
}

/// <summary>Raised broadcast when a carcass is butchered, with what it gave, before it is destroyed.</summary>
[ByRefEvent]
public readonly record struct WGButcheredEvent(EntityUid Carcass, EntityUid User, List<EntityUid> Spawned);
