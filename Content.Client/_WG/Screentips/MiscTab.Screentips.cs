using Content.Shared._WG.Screentips;

namespace Content.Client.Options.UI.Tabs;

/// <summary>The screentip settings in the Cursor section of the Misc tab.</summary>
public sealed partial class MiscTab
{
    private void WgAddScreentipOptions()
    {
        Control.AddOptionDropDown(
            ScreentipCVars.Mode,
            DropDownScreentips,
            new List<OptionDropDownCVar<int>.ValueOption>
            {
                new((int) ScreentipMode.Off, Loc.GetString("ui-options-wg-screentips-off")),
                new((int) ScreentipMode.TopOfView, Loc.GetString("ui-options-wg-screentips-top")),
                new((int) ScreentipMode.NextToCursor, Loc.GetString("ui-options-wg-screentips-cursor")),
            });
        Control.AddOptionColorSlider(ScreentipCVars.Color, ScreentipColorSlider);
    }
}
