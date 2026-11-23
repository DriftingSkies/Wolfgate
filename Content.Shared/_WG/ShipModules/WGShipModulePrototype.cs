using Content.Shared.Materials;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// A ship module a drydock fits into one of a ship's slots. The pilot fires it from the helm; it holds charges and
/// refills them while the ship has a powered helm.
/// </summary>
[Prototype("wgShipModule")]
public sealed partial class WGShipModulePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Full name, shown at the drydock.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>What it does, shown at the drydock.</summary>
    [DataField(required: true)]
    public LocId Description;

    /// <summary>Short name on the helm's module strip.</summary>
    [DataField(required: true)]
    public LocId Label;

    /// <summary>Icon shown at the drydock.</summary>
    [DataField(required: true)]
    public SpriteSpecifier Icon = SpriteSpecifier.Invalid;

    /// <summary>Credits a drydock charges to fit it.</summary>
    [DataField(required: true)]
    public int Price;

    /// <summary>Material units a drydock that pays in materials bills to fit it.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Materials = new();

    /// <summary>Uses it can hold.</summary>
    [DataField]
    public int MaxCharges = 1;

    /// <summary>Time it takes to refill one charge while the ship has a powered helm.</summary>
    [DataField]
    public TimeSpan RechargeTime = TimeSpan.FromSeconds(10);

    /// <summary>Shortest time between two uses.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    /// <summary>Whether a shield generator switched on aboard locks it, field up or not. A deflector never does.</summary>
    [DataField]
    public bool BlockedByShields;

    /// <summary>How long it stays locked after the ship's last shield generator is switched off.</summary>
    [DataField]
    public TimeSpan ShieldLockout = TimeSpan.FromSeconds(5);

    /// <summary>Played through the whole hull on each use.</summary>
    [DataField]
    public SoundSpecifier? ActivateSound;

    /// <summary>How hard each use shakes the crew's view and the ship's helm screens; 0 for none.</summary>
    [DataField]
    public float Jolt = 1f;

    /// <summary>What it does when fired; a server system handles each kind.</summary>
    [DataField(required: true)]
    public WGShipModuleEffect Effect = default!;
}

/// <summary>The kind of effect a ship module has, with its settings.</summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class WGShipModuleEffect;
