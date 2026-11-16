using Content.Shared._WF.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client.Shuttles.UI;

// Every radar screen starts with the display choices the player last made on any of them; see Content.Server/_WF/Shuttles.
public partial class ShuttleNavControl
{
    [Dependency] private IConfigurationManager _wfSettingsCfg = default!;

    private bool _wfSavedIff;
    private bool _wfSavedIffDetailed;
    private RadarAzimuthMode _wfSavedAzimuth;

    /// <summary>
    /// Starts the radar with the player's saved contact labels and bearing ring. Docks always show, as markers that
    /// stay visible from afar; their names only up close (see ShuttleNavControl.Docks.cs).
    /// </summary>
    private void WfLoadDisplaySettings()
    {
        ShowIFF = _wfSavedIff = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarShowIff);
        ShowIFFDetailed = _wfSavedIffDetailed = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarShowIffDetailed);
        ShowDocks = true;
        _azimuthMode = _wfSavedAzimuth = _wfSettingsCfg.GetCVar(WolfgateCVars.RadarAzimuth) switch
        {
            1 => RadarAzimuthMode.Ring,
            2 => RadarAzimuthMode.Off,
            _ => RadarAzimuthMode.Edge,
        };
    }

    /// <summary>Saves a display choice when the player changes it.</summary>
    private void WfSaveDisplaySettings()
    {
        if (ShowIFF == _wfSavedIff && ShowIFFDetailed == _wfSavedIffDetailed &&
            _azimuthMode == _wfSavedAzimuth)
            return;

        _wfSettingsCfg.SetCVar(WolfgateCVars.RadarShowIff, _wfSavedIff = ShowIFF);
        _wfSettingsCfg.SetCVar(WolfgateCVars.RadarShowIffDetailed, _wfSavedIffDetailed = ShowIFFDetailed);
        _wfSettingsCfg.SetCVar(WolfgateCVars.RadarAzimuth, (int) (_wfSavedAzimuth = _azimuthMode));
        _wfSettingsCfg.SaveToFile();
    }
}
