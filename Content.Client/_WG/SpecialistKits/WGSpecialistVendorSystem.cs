using Content.Client._RMC14.Vendors;
using Content.Shared._RMC14.Vendors;
using Content.Shared._WG.SpecialistKits;
using Robust.Client.GameObjects;

namespace Content.Client._WG.SpecialistKits;

/// <summary>Redraws an open specialist rack when the server's count of slots left changes.</summary>
public sealed class WGSpecialistVendorSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSpecialistVendorComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<WGSpecialistVendorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_ui.TryGetOpenUi<CMAutomatedVendorBui>(ent.Owner, CMAutomatedVendorUI.Key, out var bui))
            bui.Refresh();
    }
}
