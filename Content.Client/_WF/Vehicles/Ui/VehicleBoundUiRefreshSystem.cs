using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;

namespace Content.Client._WF.Vehicles.Ui;

/// <summary>Redraws an open vehicle window when the component state it reads arrives.</summary>
public sealed partial class VehicleBoundUiRefreshSystem : EntitySystem
{
    [Dependency] private VehicleUserInterfaceSystem _vehicleUi = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HardpointSlotsComponent, AfterAutoHandleStateEvent>(OnHardpointState);
        SubscribeLocalEvent<VehicleWeaponsSeatComponent, AfterAutoHandleStateEvent>(OnWeaponsSeatState);
        SubscribeLocalEvent<VehicleAmmoLoaderComponent, AfterAutoHandleStateEvent>(OnAmmoLoaderState);
    }

    private void OnAmmoLoaderState(Entity<VehicleAmmoLoaderComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _vehicleUi.RefreshUIs<VehicleAmmoLoaderBoundUserInterface>(ent.Owner);
    }

    private void OnWeaponsSeatState(Entity<VehicleWeaponsSeatComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _vehicleUi.RefreshUIs<VehicleWeaponsBoundUserInterface>(ent.Owner);
    }

    private void OnHardpointState(Entity<HardpointSlotsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _vehicleUi.RefreshUIs<HardpointBoundUserInterface>(ent.Owner);
    }
}
