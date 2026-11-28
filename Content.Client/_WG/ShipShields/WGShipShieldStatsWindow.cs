using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Shared._WG.ShipShields;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WG.ShipShields;

/// <summary>Shows the installed generator's operating limits without crowding allocation controls.</summary>
public sealed class WGShipShieldStatsWindow : DefaultWindow
{
    private readonly RichTextLabel _name = new();
    private readonly RichTextLabel _capacity = new();
    private readonly RichTextLabel _limit = new();
    private readonly RichTextLabel _repair = new();
    private readonly RichTextLabel _recharge = new();
    private readonly RichTextLabel _idle = new();
    private readonly RichTextLabel _maximum = new();
    private readonly RichTextLabel _lockout = new();
    private readonly RichTextLabel _costs = new();
    private readonly RichTextLabel _note = new();

    /// <summary>Creates a compact scrolling generator specification panel, with instrument chrome only for instrument consoles.</summary>
    public WGShipShieldStatsWindow(bool instruments = true)
    {
        Title = Loc.GetString("wg-shield-stats-title");
        MinSize = new Vector2(340f, 300f);
        SetSize = new Vector2(440f, 420f);
        var scroll = new ScrollContainer { HScrollEnabled = false, ReserveScrollbarSpace = true };
        var values = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 12,
            Margin = new Thickness(8),
            HorizontalExpand = true,
        };
        foreach (var label in new[] { _name, _capacity, _limit, _repair, _recharge, _idle, _maximum, _lockout, _costs })
        {
            label.HorizontalExpand = true;
            values.AddChild(instruments ? (Control) new WGGlassReadout(label) : label);
        }
        values.AddChild(_note);
        scroll.AddChild(values);
        Contents.AddChild(scroll);
        if (instruments)
            WGInstrumentTheme.Install(this);
    }

    /// <summary>Displays authoritative runtime values for the selected installed emitter.</summary>
    public void UpdateStats(WGShipShieldGeneratorStats stats)
    {
        _name.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wg-shield-stats-generator", ("name", stats.Name))));
        _capacity.Text = stats.HullCapacity > 0f
            ? Loc.GetString("wg-shield-stats-ship-capacity", ("value", MathF.Round(stats.Capacity, 0)),
                ("hull", MathF.Round(stats.HullCapacity, 0)), ("total", MathF.Round(stats.Capacity + stats.HullCapacity, 0)))
            : Loc.GetString("wg-shield-stats-capacity", ("value", MathF.Round(stats.Capacity, 0)));
        _limit.Text = Loc.GetString("wg-shield-stats-overload-limit", ("value", MathF.Round(stats.DamageLimit, 0)));
        _repair.Text = Loc.GetString(
            stats.HullCooling > 0f ? "wg-shield-stats-ship-cooling" : stats.Vents ? "wg-shield-stats-cooling" : "wg-shield-stats-online-repair",
            ("value", WGShipShieldEffects.Display(stats.HealPerSecond)), ("hull", WGShipShieldEffects.Display(stats.HullCooling)),
            ("down", WGShipShieldEffects.Display(stats.HealPerSecond * WGShipShieldEffects.FieldDownCooling)),
            ("hold", WGShipShieldEffects.Display(stats.HardHeatHold)));
        _recharge.Text = Loc.GetString(stats.Vents ? "wg-shield-stats-vent" : "wg-shield-stats-offline-repair",
            ("value", WGShipShieldEffects.Display(stats.RechargePerSecond)));
        _idle.Text = Loc.GetString("wg-shield-stats-idle-power", ("value", WGShipShieldEffects.Display(stats.BasePowerWatts / 1000f)));
        _maximum.Text = Loc.GetString("wg-shield-stats-max-power", ("value", WGShipShieldEffects.Display(stats.MaximumPowerWatts / 1000f)));
        _lockout.Text = Loc.GetString("wg-shield-stats-overload-lockout", ("value", WGShipShieldEffects.Display(stats.OverloadSeconds)));
        var costs = stats.ThrustFactor < 1f || stats.TurnFactor < 1f || stats.GunLockSeconds > 0f
            ? Loc.GetString("wg-shield-stats-costs", ("thrust", MathF.Round((1f - stats.ThrustFactor) * 100f)),
                ("turn", MathF.Round((1f - stats.TurnFactor) * 100f)), ("seconds", WGShipShieldEffects.Display(stats.GunLockSeconds)))
            : Loc.GetString("wg-shield-stats-no-costs");
        _costs.Text = stats.CollapseHeat > 0f
            ? $"{costs} {Loc.GetString("wg-shield-collapse-heat", ("share", MathF.Round(stats.CollapseHeat * 100f)))}"
            : costs;
        _note.Text = Loc.GetString("wg-shield-stats-recovery-note", ("rate", WGShipShieldEffects.Display(stats.HitHeat)));
    }
}
