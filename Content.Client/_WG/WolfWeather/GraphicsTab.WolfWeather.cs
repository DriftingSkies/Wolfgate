using Content.Shared._WG.WolfWeather;

namespace Content.Client.Options.UI.Tabs;

/// <summary>The weather detail option on the graphics tab.</summary>
public sealed partial class GraphicsTab
{
    private void WgAddWeatherQuality()
    {
        Control.AddOptionDropDown(
            WolfWeatherCVars.WeatherQuality,
            DropDownWeatherQuality,
            new List<OptionDropDownCVar<int>.ValueOption>
            {
                new(0, Loc.GetString("ui-options-wolf-weather-quality-off")),
                new(1, Loc.GetString("ui-options-wolf-weather-quality-reduced")),
                new(2, Loc.GetString("ui-options-wolf-weather-quality-full")),
            });
    }
}
