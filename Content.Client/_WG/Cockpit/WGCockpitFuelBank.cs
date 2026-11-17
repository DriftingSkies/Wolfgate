#nullable enable

using Content.Client._WG.CombatConsole;
using Content.Shared._WG.Shuttles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WG.Cockpit;

/// <summary>Pairs the ship's normalized fuel reserve with an independent low-fuel warning lens.</summary>
public sealed class WGCockpitFuelBank : BoxContainer
{
    private readonly Func<ShipFuelSummary?> _read;
    private readonly WGGlassGauge _gauge;
    private readonly WGCockpitFuelLamp _lamp;
    private float _poll;
    private int? _sources;
    private int? _unknown;
    private bool _initialized;

    /// <summary>Preserves missing and incomplete telemetry rather than displaying a false empty tank.</summary>
    public ShipFuelSummary? Reading => _read();

    /// <summary>Reads a ship's fuel summary; the prefix names the bank and its parts, so two banks in one window stay distinguishable.</summary>
    public WGCockpitFuelBank(Func<ShipFuelSummary?> read, string prefix = "Cockpit")
    {
        _read = read;
        Name = prefix + "FuelBank";
        HorizontalExpand = true;
        SeparationOverride = 6;
        SetHeight = 44;
        _gauge = new WGGlassGauge("wg-cockpit-fuel", GaugeReading, true)
        {
            Name = prefix + "Fuel",
            CompactStrip = true,
            SetHeight = 44,
            MouseFilter = MouseFilterMode.Pass,
        };
        _lamp = new WGCockpitFuelLamp(() => Reading) { Name = prefix + "FuelLow" };
        AddChild(_gauge);
        AddChild(_lamp);
        RefreshTooltip();
    }

    private WGGaugeReading GaugeReading()
    {
        var reading = Reading;
        return WGGaugeReading.Number(reading is { Available: true } fuel ? fuel.Fraction * 100 : null,
            0, 100, "wg-gauge-unit-percent", decimals: 1,
            tint: reading is { Low: true } ? WGInstrumentTheme.Skin.Caution : WGInstrumentTheme.Green);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _poll += args.DeltaSeconds;
        if (_poll < 0.1f)
            return;
        _poll %= 0.1f;
        RefreshTooltip();
    }

    private void RefreshTooltip()
    {
        var reading = Reading;
        if (_initialized && _sources == reading?.Sources && _unknown == reading?.UnknownSources)
            return;
        _initialized = true;
        _sources = reading?.Sources;
        _unknown = reading?.UnknownSources;
        var status = reading switch
        {
            null => Loc.GetString("wg-cockpit-fuel-no-signal"),
            { Sources: 0 } => Loc.GetString("wg-cockpit-fuel-no-sources"),
            { UnknownSources: > 0 } fuel => Loc.GetString("wg-cockpit-fuel-incomplete",
                ("unknown", fuel.UnknownSources), ("sources", fuel.Sources)),
            { } fuel => Loc.GetString("wg-cockpit-fuel-sources", ("sources", fuel.Sources)),
        };
        ToolTip = status + "\n" + Loc.GetString("wg-cockpit-fuel-help") + "\n" +
            Loc.GetString("wg-cockpit-fuel-low-help", ("low", MathF.Round(ShipFuelSummary.LowThreshold * 100)),
                ("critical", MathF.Round(ShipFuelSummary.CriticalThreshold * 100)),
                ("emergency", MathF.Round(ShipFuelSummary.EmergencyThreshold * 100)));
        _gauge.ToolTip = ToolTip;
        _lamp.ToolTip = ToolTip;
    }
}
