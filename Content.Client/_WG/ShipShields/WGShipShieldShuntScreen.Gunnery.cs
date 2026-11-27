using System.Numerics;
using Content.Client._WG.Cockpit;
using Robust.Client.UserInterface;
using InstrumentTheme = Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._WG.ShipShields;

public sealed partial class WGShipShieldShuntScreen
{
    /// <summary>
    /// Rebuilds this screen as the gunnery console's short shield column: the dial, deployment, heat, vent, arc and
    /// power, and none of the helm page's detail. A ship with no generator keeps only its heat and vent. The change is
    /// for good, so use a screen made for it.
    /// </summary>
    public Control WgGunneryShield()
    {
        // A lease is only a way to lift the live controls out with their bindings; this one is never handed back.
        var lease = new WGCockpitLease();
        lease.Clear(this);
        Visible = true;
        Margin = new Thickness(0);
        SeparationOverride = 6;
        var dial = lease.Take(_dial);
        dial.SetSize = new Vector2(132);
        dial.HorizontalExpand = false;
        dial.HorizontalAlignment = HAlignment.Center;
        var allocation = lease.Take(_amount);
        allocation.Visible = true;
        var arc = lease.Take(_arc);
        _wgCockpitCompact = true;
        var arcRow = InstrumentTheme.Row(InstrumentTheme.Label("wg-cockpit-shield-arc"), arc);
        AddChild(dial);
        AddChild(lease.Take(_enabled));
        AddChild(lease.Take(_heat));
        AddChild(lease.Take(_vent));
        AddChild(arcRow);
        AddChild(allocation);
        AddChild(lease.Take(_concentration));
        _heat.MinHeight = HeatGauge.CompactHeight;
        _heat.Legend = false;
        _generatorOnly.Clear();
        _compactGeneratorOnly.AddRange(new Control[] { dial, _enabled, arcRow, allocation, _concentration });
        UpdateState(_state, _helmRotation);
        AddChild(lease.Take(_gunLock, restoreVisibility: false));
        return this;
    }
}
