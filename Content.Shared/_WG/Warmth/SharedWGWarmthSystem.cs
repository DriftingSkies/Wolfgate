using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;

namespace Content.Shared._WG.Warmth;

/// <summary>The slowdown the cold and heat warnings bring, and how a body feel reads and colours.</summary>
public abstract class SharedWGWarmthSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    protected IConfigurationManager Cfg => _cfg;
    protected MovementSpeedModifierSystem Movement => _movement;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGWarmthComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<WGWarmthComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var modifier = ent.Comp.Strain switch
        {
            WGWarmthStrain.Cold => _cfg.GetCVar(WGWarmthCVars.ColdSlowdown),
            WGWarmthStrain.Hot => _cfg.GetCVar(WGWarmthCVars.HeatSlowdown),
            _ => 1f,
        };

        if (modifier < 1f)
            args.ModifySpeed(modifier, modifier);
    }

    /// <summary>The Fluent id for a body feel.</summary>
    public static string FeelLoc(WGBodyFeel feel) => feel switch
    {
        WGBodyFeel.Freezing => "wg-warmth-feel-freezing",
        WGBodyFeel.Cold => "wg-warmth-feel-cold",
        WGBodyFeel.Chilly => "wg-warmth-feel-chilly",
        WGBodyFeel.Warm => "wg-warmth-feel-warm",
        WGBodyFeel.Hot => "wg-warmth-feel-hot",
        WGBodyFeel.Overheating => "wg-warmth-feel-overheating",
        _ => "wg-warmth-feel-comfortable",
    };

    /// <summary>A body feel's colour by danger: green comfortable, pale mild, amber warning, red harmful.</summary>
    public static Color FeelColor(WGBodyFeel feel) => feel switch
    {
        WGBodyFeel.Freezing or WGBodyFeel.Overheating => Color.FromHex("#FF5A5A"),
        WGBodyFeel.Cold or WGBodyFeel.Hot => Color.FromHex("#FFA040"),
        WGBodyFeel.Chilly or WGBodyFeel.Warm => Color.FromHex("#E6DC96"),
        _ => Color.FromHex("#8FD694"),
    };

    /// <summary>The "Here" reading: the tile's air temperature, or vacuum.</summary>
    public static string HereText(WGWarmthComponent comp) => comp.HereCelsius is { } celsius
        ? Robust.Shared.Localization.Loc.GetString("wg-warmth-here", ("celsius", celsius))
        : Robust.Shared.Localization.Loc.GetString("wg-warmth-here-vacuum");
}
