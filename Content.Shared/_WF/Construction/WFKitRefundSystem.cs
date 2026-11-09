using Content.Shared.Construction;
using Robust.Shared.Network;

namespace Content.Shared._WF.Construction;

/// <summary>
/// Remembers the kits used on a machine (upgrade kits, conversion kits) and spawns them back when the machine is
/// deconstructed, instead of losing them with it.
/// </summary>
public sealed partial class WFKitRefundSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFKitRefundComponent, MachineDeconstructedEvent>(OnDeconstructed);
    }

    /// <summary>Records a kit spent on a machine. Server only, like the kit's deletion.</summary>
    public void RecordKit(EntityUid machine, EntityUid kit)
    {
        if (_net.IsClient || MetaData(kit).EntityPrototype?.ID is not { } proto)
            return;

        EnsureComp<WFKitRefundComponent>(machine).Kits.Add(proto);
    }

    private void OnDeconstructed(EntityUid uid, WFKitRefundComponent comp, MachineDeconstructedEvent args)
    {
        if (_net.IsClient)
            return;

        var coords = Transform(uid).Coordinates;
        foreach (var kit in comp.Kits)
        {
            Spawn(kit, coords);
        }

        comp.Kits.Clear();
    }
}
