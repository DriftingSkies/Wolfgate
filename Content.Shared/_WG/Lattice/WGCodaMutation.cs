using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Lattice;

/// <summary>A person whose arm the Coda has twisted into a blade.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGCodaMutatedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Blade;

    /// <summary>When they last left the Coda with it, or null while inside. Server only.</summary>
    [DataField]
    public TimeSpan? OutSince;
}

/// <summary>On a surgery: only for someone with a twisted arm.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGTwistedArmConditionComponent : Component;

/// <summary>On a surgery step: cuts the twisted arm away. Done once the patient no longer has it.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGTwistedArmRemovalComponent : Component
{
    /// <summary>What is left of it afterwards.</summary>
    [DataField]
    public string Tissue = "WGRemadeTissue";
}

/// <summary>The twisted arm surgery's checks, which the surgery window also runs on the client.</summary>
public sealed class WGTwistedArmConditionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTwistedArmConditionComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<WGTwistedArmRemovalComponent, SurgeryStepCompleteCheckEvent>(OnCheck);
    }

    private void OnValid(Entity<WGTwistedArmConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!HasComp<WGCodaMutatedComponent>(args.Body))
            args.Cancelled = true;
    }

    private void OnCheck(Entity<WGTwistedArmRemovalComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasComp<WGCodaMutatedComponent>(args.Body))
            args.Cancelled = true;
    }
}
