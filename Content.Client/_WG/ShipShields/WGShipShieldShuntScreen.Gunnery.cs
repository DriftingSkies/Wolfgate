using System.Numerics;
using Content.Client._WG.Cockpit;
using Robust.Client.UserInterface;
using InstrumentTheme = Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._WG.ShipShields;

public sealed partial class WGShipShieldShuntScreen
{
    /// <summary>
    /// Rebuilds this screen as the gunnery console's short shield column: the dial, deployment, arc, power and
    /// integrity, and none of the helm page's detail. The change is for good, so use a screen made for it.
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
        _health.MinHeight = 12;
        AddChild(dial);
        AddChild(lease.Take(_enabled));
        AddChild(InstrumentTheme.Row(InstrumentTheme.Label("wg-cockpit-shield-arc"), arc));
        AddChild(allocation);
        AddChild(lease.Take(_concentration));
        AddChild(lease.Take(_health));
        return this;
    }
}
