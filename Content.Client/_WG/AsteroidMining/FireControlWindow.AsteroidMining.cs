using System.Linq;
using Content.Client._WG.AsteroidMining;
using Content.Shared._WG.AsteroidMining;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;

namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    /// <summary>How long a power step shows before the server's value, in case the server never applies it.</summary>
    private static readonly TimeSpan WgMiningPendingTimeout = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _wgTiming = default!;

    private Control _wgMiningPanel = default!;
    private Label _wgMiningPower = default!;
    private Label _wgMiningStatus = default!;
    private Label _wgMiningIdle = default!;
    private Control _wgMiningScan = default!;
    private Label _wgMiningRock = default!;
    private BoxContainer _wgMiningOres = default!;
    private readonly List<Label> _wgMiningOreLabels = new();
    private Label _wgMiningInert = default!;
    private Label _wgMiningResistance = default!;
    private Label _wgMiningHeat = default!;
    private Label _wgMiningYield = default!;
    private WGMiningChargeMeter _wgMiningMeter = default!;
    private ProgressBar _wgMiningProgress = default!;
    private Label _wgMiningOvercharge = default!;
    private WGMiningBeamGunneryState _wgMining;
    private float? _wgMiningPending;
    private TimeSpan _wgMiningPendingUntil;

    /// <summary>
    /// Builds the gunner's mining laser panel: the ship's laser power and a scan of the rock under fire. It stays hidden
    /// until the ship reports a mining laser.
    /// </summary>
    private Control WgCreateMiningPanel()
    {
        _wgMiningPower = Label("wg-mining-gunnery-power", Cream);
        _wgMiningPower.ToolTip = Loc.GetString("wg-mining-gunnery-power-help");
        _wgMiningPower.MouseFilter = MouseFilterMode.Pass;
        var steps = WGMiningGauges.PowerStepButtons(WgStepMiningPower);

        _wgMiningStatus = Label("wg-mining-gunnery-idle", Muted);
        _wgMiningStatus.ClipText = true;
        _wgMiningIdle = Label("wg-mining-gunnery-idle", Muted);
        _wgMiningIdle.ClipText = true;
        _wgMiningIdle.ToolTip = Loc.GetString("wg-mining-gunnery-idle-help");
        _wgMiningIdle.MouseFilter = MouseFilterMode.Pass;

        _wgMiningRock = Label("wg-mining-gunnery-idle", Cream);
        _wgMiningRock.ClipText = true;
        _wgMiningOres = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        _wgMiningInert = Label("wg-mining-gunnery-idle", Muted);
        _wgMiningResistance = Label("wg-mining-gunnery-idle", Muted);
        _wgMiningHeat = Label("wg-mining-gunnery-idle", Cream);
        _wgMiningHeat.ClipText = true;
        _wgMiningYield = Label("wg-mining-gunnery-idle", Green);
        _wgMiningYield.ClipText = true;
        _wgMiningYield.ToolTip = Loc.GetString("wg-mining-gunnery-yield-help");
        _wgMiningYield.MouseFilter = MouseFilterMode.Pass;
        _wgMiningMeter = new WGMiningChargeMeter { ToolTip = Loc.GetString("wg-mining-gunnery-meter-help") };
        _wgMiningProgress = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 12, HorizontalExpand = true };
        _wgMiningOvercharge = Label("wg-mining-gunnery-overcharge", Red);
        _wgMiningOvercharge.ClipText = true;
        _wgMiningOvercharge.ToolTip = Loc.GetString("wg-mining-gunnery-overcharge-help");
        _wgMiningOvercharge.MouseFilter = MouseFilterMode.Pass;
        _wgMiningOvercharge.Visible = false;
        var scan = Column(_wgMiningRock, _wgMiningOres, _wgMiningInert, _wgMiningResistance, _wgMiningHeat, _wgMiningMeter,
            Label("wg-mining-gunnery-progress", Muted), _wgMiningProgress, _wgMiningYield, _wgMiningOvercharge);
        scan.SeparationOverride = 2;
        scan.Visible = false;
        _wgMiningScan = scan;

        var body = Column(_wgMiningPower, steps, _wgMiningStatus, _wgMiningIdle, scan);
        body.SeparationOverride = 4;
        var panel = Panel("wg-mining-gunnery-title", body);
        panel.Name = "WgGunneryMining";
        panel.SetWidth = WgShieldPanelWidth;
        panel.HorizontalExpand = false;
        panel.Visible = false;
        return _wgMiningPanel = panel;
    }

    private void WgStepMiningPower(float step)
    {
        if (_wgMining.Lasers == 0)
            return;

        var power = MathF.Round(Math.Clamp((_wgMiningPending ?? _wgMining.Power) + step, 0f, 1f), 2);
        _wgMiningPending = power;
        _wgMiningPendingUntil = _wgTiming.RealTime + WgMiningPendingTimeout;
        CombatMessage?.Invoke(new WGMiningBeamPowerMessage(power));
        WgRefreshMining();
    }

    /// <summary>Refreshes the mining laser panel from the server's scan.</summary>
    public void WgUpdateMining(WGMiningBeamGunneryState state)
    {
        _wgMining = state;
        if (_wgMiningPending is { } pending &&
            (MathF.Abs(pending - state.Power) < 0.005f || _wgTiming.RealTime > _wgMiningPendingUntil))
            _wgMiningPending = null;
        WgRefreshMining();
    }

    private void WgRefreshMining()
    {
        var state = _wgMining;
        _wgMiningPanel.Visible = state.Lasers > 0;
        // The panel takes the column's slack, not the shield panel over it, so the scan grows down and the power steps stay put.
        _wgMiningPanel.VerticalExpand = state.Lasers > 0;
        _wgShieldPanel.VerticalExpand = state.Lasers == 0;
        if (state.Lasers == 0)
            return;

        var power = _wgMiningPending ?? state.Power;
        _wgMiningPower.Text = Loc.GetString("wg-mining-gunnery-power", ("power", (int) MathF.Round(power * 100)));
        _wgMiningStatus.Text = state.Cooldown > 0f
            ? Loc.GetString("wg-mining-gunnery-cooldown", ("seconds", (int) MathF.Ceiling(state.Cooldown)))
            : Loc.GetString("wg-mining-gunnery-lasers", ("count", state.Lasers));
        _wgMiningStatus.FontColorOverride = state.Cooldown > 0f ? Red : Muted;

        _wgMiningIdle.Visible = state.Target == null;
        _wgMiningScan.Visible = state.Target != null;
        if (state.Target is not { } rock)
            return;

        _wgMiningRock.Text = Loc.GetString("wg-mining-gunnery-rock", ("name", rock.Name), ("mass", (int) MathF.Round(rock.Mass)));
        WGMiningGauges.ShowOres(_wgMiningOres, _wgMiningOreLabels, rock.Ores);
        _wgMiningInert.Text = Loc.GetString("wg-mining-gunnery-inert", ("percent", (int) MathF.Round((1f - rock.Ores.Sum(o => o.Fraction)) * 100)));
        _wgMiningResistance.Text = Loc.GetString("wg-mining-gunnery-resistance", ("percent", (int) MathF.Round(rock.Resistance * 100)));

        _wgMiningHeat.Text = Loc.GetString("wg-mining-gunnery-heat", ("charge", (int) MathF.Round(rock.Charge * 100)),
            ("low", (int) MathF.Round(rock.WindowLow * 100)), ("high", (int) MathF.Round(rock.WindowHigh * 100)));
        _wgMiningMeter.Charge = rock.Charge;
        _wgMiningMeter.WindowLow = rock.WindowLow;
        _wgMiningMeter.WindowHigh = rock.WindowHigh;
        _wgMiningMeter.Overcharging = rock.Overcharge > 0f;
        _wgMiningProgress.Value = rock.Progress;
        _wgMiningYield.Text = Loc.GetString("wg-mining-gunnery-yield", ("percent", (int) MathF.Round(rock.Yield * 100)));
        _wgMiningYield.FontColorOverride = rock.Yield >= 0.995f ? Green : Accent;
        _wgMiningOvercharge.Visible = rock.Overcharge > 0f;
    }
}
