using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Lattice;

/// <summary>On a surgery: only for a Choir member, and not out in the band where the dead rise.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGSongExcisionConditionComponent : Component
{
    /// <summary>Least Lattice band where the song is too loud to cut out.</summary>
    [DataField]
    public int NoSurgeryBand = 4;
}

/// <summary>On a surgery step: cuts the song out. Done once the patient is no longer Choir.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGSongExcisionEffectComponent : Component
{
    /// <summary>Chance the patient dies on the table and turns Remade.</summary>
    [DataField]
    public float FailChance = 0.3f;

    /// <summary>Brain health lost either way.</summary>
    [DataField]
    public float BrainDamage = 30f;
}

/// <summary>What the song is cut out with: exotic matter, used up one shard at a time.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGSongExcisionToolComponent : Component, ISurgeryToolComponent
{
    public string ToolName => Loc.GetString("wg-song-excision-tool-name");

    public bool? Used { get; set; } = true;

    [DataField]
    public float Speed { get; set; } = 1f;
}

/// <summary>The excision's checks, which the surgery window also runs on the client.</summary>
public sealed class WGSongExcisionConditionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSongExcisionConditionComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<WGSongExcisionEffectComponent, SurgeryStepCompleteCheckEvent>(OnCheck);
    }

    private void OnValid(Entity<WGSongExcisionConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!HasComp<WGChoirMemberComponent>(args.Body) || Band(args.Body) >= ent.Comp.NoSurgeryBand)
            args.Cancelled = true;
    }

    private void OnCheck(Entity<WGSongExcisionEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasComp<WGChoirMemberComponent>(args.Body))
            args.Cancelled = true;
    }

    /// <summary>The Lattice band the patient is in: their ship's, or their own off one.</summary>
    private int Band(EntityUid body)
    {
        if (Transform(body).GridUid is { } grid && TryComp<WGDriftComponent>(grid, out var ship))
            return ship.Band;

        return CompOrNull<WGDriftComponent>(body)?.Band ?? 0;
    }
}
