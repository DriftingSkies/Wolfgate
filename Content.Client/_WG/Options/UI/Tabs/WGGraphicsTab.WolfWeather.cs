using Content.Client.Options.UI;
using Content.Shared._WG.WolfWeather;

namespace Content.Client._WG.Options.UI.Tabs;

/// <summary>The weather detail option on the Wolfgate Graphics tab.</summary>
public sealed partial class WGGraphicsTab
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
