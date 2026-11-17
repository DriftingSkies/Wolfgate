using Content.Shared._WG.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client.Shuttles.UI;

// Every radar screen starts with the display choices the player last made on any of them; see Content.Server/_WG/Shuttles.
public partial class ShuttleNavControl
{
    [Dependency] private IConfigurationManager _wgSettingsCfg = default!;

    private bool _wgSavedIff;
    private bool _wgSavedIffDetailed;
    private RadarAzimuthMode _wgSavedAzimuth;

    /// <summary>
    /// Starts the radar with the player's saved contact labels and bearing ring. Docks always show, as markers that
    /// stay visible from afar; their names only up close (see ShuttleNavControl.Docks.cs).
    /// </summary>
    private void WgLoadDisplaySettings()
    {
        ShowIFF = _wgSavedIff = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarShowIff);
        ShowIFFDetailed = _wgSavedIffDetailed = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarShowIffDetailed);
        ShowDocks = true;
        _azimuthMode = _wgSavedAzimuth = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarAzimuth) switch
        {
            1 => RadarAzimuthMode.Ring,
            2 => RadarAzimuthMode.Off,
            _ => RadarAzimuthMode.Edge,
        };
    }

    /// <summary>Saves a display choice when the player changes it.</summary>
    private void WgSaveDisplaySettings()
    {
        if (ShowIFF == _wgSavedIff && ShowIFFDetailed == _wgSavedIffDetailed &&
            _azimuthMode == _wgSavedAzimuth)
            return;

        _wgSettingsCfg.SetCVar(WolfgateCVars.RadarShowIff, _wgSavedIff = ShowIFF);
        _wgSettingsCfg.SetCVar(WolfgateCVars.RadarShowIffDetailed, _wgSavedIffDetailed = ShowIFFDetailed);
        _wgSettingsCfg.SetCVar(WolfgateCVars.RadarAzimuth, (int) (_wgSavedAzimuth = _azimuthMode));
        _wgSettingsCfg.SaveToFile();
    }
}
