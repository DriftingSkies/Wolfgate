using Content.Shared._WG.Lattice;
using Content.Shared.Bed.Sleep;
using Content.Shared.Mobs.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Lattice;

/// <summary>
/// How much of where they are a person actually feels: their charm, a grounding drug, sleep and the crew round them
/// all change it. A ship that stays long in the Coda soaks some Drift into its hull for good.
/// </summary>
public sealed partial class WGLatticeSystem
{
    /// <summary>How close another of the crew has to be to steady someone, in metres.</summary>
    private const float CompanyRange = 8f;

    /// <summary>Share of Drift each steadying crewmate takes off, and the most they can take together.</summary>
    private const float CompanyShare = 0.1f;
    private const float CompanyMax = 0.3f;

    /// <summary>How much worse it is with nobody else in sight.</summary>
    private const float AloneScale = 1.15f;

    /// <summary>Above this Drift a crewmate is too far gone to steady anyone.</summary>
    private const float SaneDrift = 70f;

    /// <summary>Share of Drift sleep keeps off: anywhere, and on a sheltered ship.</summary>
    private const float SleepShare = 0.2f;
    private const float ShelteredSleepShare = 0.5f;

    [Dependency] private WGCodaFleshSystem _flesh = default!;

    /// <summary>Works out and stores the Drift one person feels aboard a carrier.</summary>
    private float Feel(EntityUid mob, EntityUid carrier, WGDriftComponent drift, List<(ICommonSession Session, EntityUid Mob)> aboard,
        WGLatticeProfilePrototype profile)
    {
        var felt = drift.Drift;
        if (TryComp<WGLatticeShelterComponent>(mob, out var charm) && carrier != mob)
            felt *= 1f - Math.Clamp(charm.Shelter, 0f, 1f);

        if (TryComp<WGGroundedComponent>(mob, out var grounded) && _timing.CurTime < grounded.Until)
            felt *= 1f - Math.Clamp(grounded.Share, 0f, 1f);

        if (HasComp<SleepingComponent>(mob))
        {
            var sheltered = TryComp<WGLatticeShelterComponent>(carrier, out var shelter) && shelter.Shelter > 0f;
            felt *= 1f - (sheltered ? ShelteredSleepShare : SleepShare);
        }

        felt *= Company(mob, aboard);
        felt = Math.Clamp(felt, 0f, profile.DriftMax);

        var head = EnsureComp<WGDriftComponent>(mob);
        head.Felt = felt;
        head.FeltAt = _timing.CurTime;
        var level = (int) (felt / WGDriftComponent.LevelStep);
        if (head.FeltLevel != level)
        {
            head.FeltLevel = level;
            Dirty(mob, head);
        }

        return felt;
    }

    /// <summary>Each sane crewmate close by steadies a person a little; nobody at all makes it worse.</summary>
    private float Company(EntityUid mob, List<(ICommonSession Session, EntityUid Mob)> aboard)
    {
        var steady = 0;
        var at = Transform(mob).Coordinates;
        foreach (var (_, other) in aboard)
        {
            if (other == mob
                || !TryComp<MobStateComponent>(other, out var state)
                || state.CurrentState == Shared.Mobs.MobState.Dead
                || HasComp<WGChoirMemberComponent>(other)
                || CompOrNull<WGDriftComponent>(other)?.Felt >= SaneDrift
                || !_transform.InRange(at, Transform(other).Coordinates, CompanyRange))
                continue;

            steady++;
        }

        return steady == 0 ? AloneScale : 1f - Math.Min(CompanyMax, steady * CompanyShare);
    }

    /// <summary>Soaks Drift into a ship's hull while it is in the Coda, and keeps its Drift from falling below what has soaked in.</summary>
    private void Soak(EntityUid carrier, WGDriftComponent drift, int band, WGLatticeProfilePrototype profile, float seconds)
    {
        if (!HasComp<MapGridComponent>(carrier))
            return;

        if (band >= profile.HazardBand && drift.Residue < profile.ResidueMax)
            drift.Residue = Math.Min(profile.ResidueMax, drift.Residue + profile.ResidueRate * seconds / 60f);

        if (drift.Drift < drift.Residue)
            drift.Drift = drift.Residue;
    }

    /// <summary>What a drydock charges to clean the Drift soaked into a ship's hull, a whole point at least; 0 if none has.</summary>
    public int CleanDriftQuote(EntityUid ship)
    {
        if (!TryComp<WGDriftComponent>(ship, out var drift) || drift.Residue <= 0f)
            return 0;

        var price = _proto.TryIndex<WGLatticeProfilePrototype>("WGDefault", out var profile) ? profile.CleanDriftPrice : 150;
        if (Transform(ship).MapUid is { } map && TryComp<WGLatticeFieldComponent>(map, out var field)
            && _proto.TryIndex(field.Profile, out var own))
            price = own.CleanDriftPrice;

        return (int) MathF.Ceiling(drift.Residue) * price;
    }

    /// <summary>Clears a ship's soaked-in Drift and the Drift it holds, as a drydock does.</summary>
    public void CleanDrift(EntityUid ship)
    {
        if (!TryComp<WGDriftComponent>(ship, out var drift))
            return;

        drift.Residue = 0f;
        drift.Drift = 0f;
        _flesh.Clear(ship);
        drift.Level = 0;
        Dirty(ship, drift);
    }
}

/// <summary>On a person: a grounding drug keeps a share of the Drift they feel off them until it wears off.</summary>
[RegisterComponent]
public sealed partial class WGGroundedComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;

    [ViewVariables]
    public float Share;
}
