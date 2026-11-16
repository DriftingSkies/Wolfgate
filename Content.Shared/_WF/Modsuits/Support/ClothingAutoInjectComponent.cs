using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Robust.Shared.Audio;

namespace Content.Shared._WF.Modsuits.Support;

/// <summary>Lets clothing inject the wearer with reagents on a cooldown, used by the emergency injector module.</summary>
[RegisterComponent]
public sealed partial class ClothingAutoInjectComponent : Component
{
    [DataField(required: true)]
    public Dictionary<string, FixedPoint2> Reagents = new();

    [DataField]
    public bool AutoInjectOnCrit = true;

    [DataField]
    public TimeSpan AutoInjectInterval = TimeSpan.FromSeconds(120);

    [ViewVariables]
    public TimeSpan NextAutoInjectTime;

    [DataField]
    public SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");

    [DataField]
    public LocId Popup = "autoinjector-injection-hardsuit";
}

/// <summary>Relays a mob state change to worn clothing so it can react.</summary>
public record struct ClothingAutoInjectRelayedEvent(EntityUid Target, MobState NewState) : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}
