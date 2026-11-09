using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lattice;

/// <summary>A person whose arm the Coda has twisted into a blade.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCodaMutatedComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Blade;

    /// <summary>When they last left the Coda with it, or null while inside. Server only.</summary>
    [DataField]
    public TimeSpan? OutSince;
}

/// <summary>On a surgery: only for someone with a twisted arm.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFTwistedArmConditionComponent : Component;

/// <summary>On a surgery step: cuts the twisted arm away. Done once the patient no longer has it.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFTwistedArmRemovalComponent : Component
{
    /// <summary>What is left of it afterwards.</summary>
    [DataField]
    public string Tissue = "WFRemadeTissue";
}

/// <summary>The twisted arm surgery's checks, which the surgery window also runs on the client.</summary>
public sealed class WFTwistedArmConditionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFTwistedArmConditionComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<WFTwistedArmRemovalComponent, SurgeryStepCompleteCheckEvent>(OnCheck);
    }

    private void OnValid(Entity<WFTwistedArmConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!HasComp<WFCodaMutatedComponent>(args.Body))
            args.Cancelled = true;
    }

    private void OnCheck(Entity<WFTwistedArmRemovalComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasComp<WFCodaMutatedComponent>(args.Body))
            args.Cancelled = true;
    }
}
