using System.Linq;
using Content.Client._WG.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared._WG.CombatConsole;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    private WGGaugeReading WgSupplyReading(NetEntity uid)
    {
        if (_currentState is not { Connected: true } state)
            return WGGaugeReading.Number(null, 0, 1, "wg-gauge-unit-rounds");
        if (!state.Combat.WeaponSupplies.TryGetValue(uid, out var supply))
        {
            var entry = state.FireControllables?.FirstOrDefault(weapon => weapon.NetEntity == uid);
            var count = entry?.AmmoCount;
            return WGGaugeReading.Number(count, 0, WGGaugeScale.Ceiling(count ?? 0, 30),
                "wg-gauge-unit-rounds", tint: count == 0 ? Red : Accent);
        }
        if (supply.Kind == WGWeaponSupplyKind.Infinite)
            return new WGGaugeReading(1, 0, 1, Loc.GetString("wg-gauge-unlimited"), Accent);
        if (supply.Kind == WGWeaponSupplyKind.Energy)
        {
            double? charge = supply.Count is { } count && supply.Capacity is > 0 ? count * 100d / supply.Capacity : null;
            return WGGaugeReading.Number(charge, 0, 100, "wg-gauge-unit-percent", tint: charge == 0 ? Red : Accent);
        }
        var reading = WGGaugeReading.Number(supply.Count, 0, Math.Max(1, (double?) supply.Capacity ?? WGGaugeScale.Ceiling(supply.Count ?? 0, 30)),
            "wg-gauge-unit-rounds", tint: supply.Count == 0 && supply.Kind != WGWeaponSupplyKind.Recharging ? Red : Accent);
        if (supply is { Kind: WGWeaponSupplyKind.Recharging, Count: 0 })
            return reading with { Text = Loc.GetString("wg-gauge-recharging") };
        return supply is { Count: { } rounds, Capacity: > 0 }
            ? reading with { Text = Loc.GetString("wg-weapon-supply-count", ("count", rounds),
                ("capacity", supply.Capacity.Value), ("unit", Loc.GetString("wg-gauge-unit-rounds"))) }
            : reading;
    }

    /// <summary>Whether this weapon is offensive; flare launchers are not.</summary>
    internal bool WgAvailableWeapon(NetEntity uid) => _wgCombat?.FlareLaunchers.Contains(uid) != true;

    /// <summary>Styles the existing selection button once, preserving every upstream handler.</summary>
    private void WgStyleWeapon(Button button, NetEntity uid)
    {
        if (button.Children.OfType<WGWeaponRow>().Any())
            return;
        button.AddStyleClass("WgNativeStyle");
        button.AddStyleClass("WgWeapon");
        button.Margin = new Thickness(0);
        button.MinWidth = 0;
        button.MinHeight = 0;
        button.RectClipContent = true;
        button.ClipText = true;
        button.Label.Visible = false;
        button.Stylesheet = null;
        button.StyleBoxOverride = new WGWeaponRowStyleBox(button);
        button.MuteSounds = true;
        button.OnPressed -= WGConsoleAudio.Press;
        button.OnPressed += WGConsoleAudio.Press;
        button.AddChild(new WGWeaponRow(button, () => WgSupplyReading(uid)));
    }

    private bool WgUpdateWeaponSupplyText(Button button, FireControllableEntry controllable)
    {
        var reading = WgSupplyReading(controllable.NetEntity);
        if (button.Text != controllable.Name)
            button.Text = controllable.Name;
        var tip = Loc.GetString("wg-console-weapon-supply", ("name", controllable.Name), ("supply", reading.Text));
        if (button.ToolTip != tip)
            button.ToolTip = tip;
        button.ModulateSelfOverride = null;
        button.Children.OfType<WGWeaponRow>().FirstOrDefault()?.Refresh(reading);
        return true;
    }
}
