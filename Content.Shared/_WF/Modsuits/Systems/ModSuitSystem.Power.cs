using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.PowerCell.Components;
using Content.Shared.Popups;
using Content.Shared.Rounding;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared._WF.Modsuits.Systems;

public sealed partial class ModSuitSystem
{
    private static readonly TimeSpan PowerAlertInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextPowerAlertUpdate;

    public EntityUid? GetCore(Entity<ModSuitComponent> ent)
    {
        return _itemSlots.GetItemOrNull(ent, ent.Comp.CoreSlotId);
    }

    /// <summary>Returns the battery the suit draws from, either in the core itself or in its cell slot.</summary>
    public Entity<BatteryComponent>? GetBattery(Entity<ModSuitComponent> ent)
    {
        if (GetCore(ent) is not { } core)
            return null;

        return TryGetBattery(core);
    }

    /// <summary>Finds a battery on the entity or in the power cell slot it owns.</summary>
    public Entity<BatteryComponent>? TryGetBattery(EntityUid uid)
    {
        if (TryComp<BatteryComponent>(uid, out var battery))
            return (uid, battery);

        if (!TryComp<PowerCellSlotComponent>(uid, out var cellSlot)
            || _itemSlots.GetItemOrNull(uid, cellSlot.CellSlotId) is not { } cell
            || !TryComp<BatteryComponent>(cell, out var cellBattery))
            return null;

        return (cell, cellBattery);
    }

    /// <summary>Reads a battery's charge through the shared power event, which the server implements.</summary>
    public float GetCharge(Entity<BatteryComponent> battery)
    {
        var ev = new GetChargeEvent();
        RaiseLocalEvent(battery.Owner, ref ev);
        return ev.CurrentCharge;
    }

    public bool HasCharge(Entity<ModSuitComponent> ent, float amount)
    {
        if (_net.IsClient)
            return ent.Comp.Charge >= amount;

        return GetBattery(ent) is { } battery && GetCharge(battery) >= amount;
    }

    public bool TryUseCharge(Entity<ModSuitComponent> ent, float amount)
    {
        if (amount <= 0f)
            return true;

        if (_net.IsClient)
            return ent.Comp.Charge >= amount;

        return GetBattery(ent) is { } battery && _battery.TryUseCharge(battery.AsNullable(), amount);
    }

    /// <summary>Copies the battery's charge into networked fields so the client can display it.</summary>
    private void SyncCharge(Entity<ModSuitComponent> ent)
    {
        if (_net.IsClient)
            return;

        var charge = 0f;
        var max = 1f;
        var hasBattery = false;
        if (GetBattery(ent) is { } battery)
        {
            charge = GetCharge(battery);
            max = MathF.Max(1f, battery.Comp.MaxCharge);
            hasBattery = true;
        }

        if (MathF.Abs(ent.Comp.Charge - charge) < 0.01f
            && MathF.Abs(ent.Comp.MaxCharge - max) < 0.01f
            && ent.Comp.HasBattery == hasBattery)
            return;

        ent.Comp.Charge = charge;
        ent.Comp.MaxCharge = max;
        ent.Comp.HasBattery = hasBattery;
        Dirty(ent);
    }

    public float GetThemeDrain(Entity<ModSuitComponent> ent)
    {
        return _proto.Index(ent.Comp.Theme).ChargeDrain;
    }

    public float GetTotalDrain(Entity<ModSuitComponent> ent)
    {
        if (ent.Comp.State == ModSuitState.Inactive)
            return 0f;

        var drain = GetThemeDrain(ent);
        if (ent.Comp.State != ModSuitState.Active)
            return drain;

        foreach (var module in ent.Comp.ModulesContainer.ContainedEntities)
        {
            if (!TryComp<ModModuleComponent>(module, out var moduleComp))
                continue;

            drain += moduleComp.IdlePower;
            if (moduleComp.Active)
                drain += moduleComp.ActivePower;
        }

        return drain;
    }

    /// <summary>Wolfgate's batteries have no charge-rate hook, so the drain is applied in <see cref="UpdatePowerDrain"/>.</summary>
    public void RefreshDrain(Entity<ModSuitComponent> ent)
    {
        SyncCharge(ent);
    }

    private void OnPowerCellChanged(Entity<ModSuitComponent> ent, ref PowerCellChangedEvent args)
    {
        if (_timing.ApplyingState || ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        RaiseUpdated(ent);
        SyncCharge(ent);
        SetPowered(ent, !args.Ejected && HasCharge(ent, 0.01f));
        UpdatePowerAlert(ent);
    }

    private void OnSuitEntInserted(Entity<ModSuitComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (ent.Comp.LifeStage < ComponentLifeStage.Running)
            return;

        RaiseUpdated(ent);
        SyncCharge(ent);

        if (args.Container.ID != ent.Comp.CoreSlotId || _timing.ApplyingState)
            return;

        RefreshDrain(ent);
        SetPowered(ent, HasCharge(ent, 0.01f));
    }

    private void OnSuitEntRemoved(Entity<ModSuitComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (ent.Comp.LifeStage < ComponentLifeStage.Running)
            return;

        RaiseUpdated(ent);
        SyncCharge(ent);

        if (args.Container.ID != ent.Comp.CoreSlotId || _timing.ApplyingState)
            return;

        SetPowered(ent, false);
    }

    private void RaiseUpdated(EntityUid suit)
    {
        var ev = new ModSuitUpdatedEvent();
        RaiseLocalEvent(suit, ref ev);
    }

    private void SetPowered(Entity<ModSuitComponent> ent, bool powered)
    {
        if (ent.Comp.Powered == powered)
            return;

        ent.Comp.Powered = powered;
        Dirty(ent);

        if (ent.Comp.State == ModSuitState.Active)
        {
            if (powered)
            {
                ActivatePassiveModules(ent);
            }
            else
            {
                DeactivateAllModules(ent, true);
                if (ent.Comp.Wearer is { } wearer)
                    _popup.PopupClient(Loc.GetString("modsuit-out-of-power"), wearer, wearer, PopupType.MediumCaution);
            }
        }

        RefreshDrain(ent);
        RefreshSpeed(ent);
        UpdatePowerAlert(ent);
    }

    private void UpdatePowerAlerts()
    {
        if (_timing.CurTime < _nextPowerAlertUpdate)
            return;

        _nextPowerAlertUpdate = _timing.CurTime + PowerAlertInterval;

        var query = EntityQueryEnumerator<ModSuitComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!ShouldStep(comp))
                continue;

            var ent = (uid, comp);
            if (comp.Wearer != null && comp.State != ModSuitState.Inactive)
            {
                UpdatePowerDrain(ent);
                UpdatePowerAlert(ent);
            }

            SyncCharge(ent);
        }
    }

    /// <summary>Spends the suit's per-second drain on the server and cuts power when the core empties.</summary>
    private void UpdatePowerDrain(Entity<ModSuitComponent> ent)
    {
        if (_net.IsClient || GetBattery(ent) is not { } battery)
            return;

        var charge = GetCharge(battery);
        if (charge <= 0f)
        {
            SetPowered(ent, false);
            return;
        }

        var drain = GetTotalDrain(ent);
        if (drain <= 0f)
            return;

        if (!_battery.TryUseCharge(battery.AsNullable(), drain) || GetCharge(battery) <= 0f)
            SetPowered(ent, false);
    }

    private void UpdatePowerAlert(Entity<ModSuitComponent> ent)
    {
        if (_timing.ApplyingState || ent.Comp.Wearer is not { } wearer)
            return;

        if (ent.Comp.State == ModSuitState.Inactive || GetCore(ent) == null)
        {
            _alerts.ClearAlert(wearer, ent.Comp.PowerAlert);
            return;
        }

        var severity = ContentHelpers.RoundToLevels(MathF.Max(0f, ent.Comp.Charge), MathF.Max(1f, ent.Comp.MaxCharge), 6);
        _alerts.ShowAlert(wearer, ent.Comp.PowerAlert, (short) severity);
    }
}
