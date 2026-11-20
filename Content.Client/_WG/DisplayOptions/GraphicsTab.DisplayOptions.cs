using Content.Shared._Scp.ScpCCVars;
using Content.Shared.CCVar;
using Robust.Shared;

namespace Content.Client.Options.UI.Tabs;

/// <summary>Graphics settings that existed as cvars without a control: FPS cap, space background, bloom, grain.</summary>
public sealed partial class GraphicsTab
{
    private static readonly int[] WgFpsCaps = { 30, 60, 75, 90, 120, 144, 165, 240 };

    private void WgAddDisplayOptions()
    {
        var caps = new List<OptionDropDownCVar<int>.ValueOption>
        {
            new(0, Loc.GetString("ui-options-wg-max-fps-unlimited")),
        };
        foreach (var cap in WgFpsCaps)
        {
            caps.Add(new(cap, Loc.GetString("ui-options-wg-max-fps-value", ("fps", cap))));
        }

        Control.AddOptionDropDown(CVars.DisplayMaxFPS, DropDownMaxFps, caps);
        Control.AddOptionCheckBox(CCVars.ParallaxEnabled, ParallaxEnabledCheckBox);
        Control.AddOptionCheckBox(ScpCCVars.LightBloomEnable, BloomCheckBox);
        Control.AddOptionPercentSlider(ScpCCVars.LightBloomStrength, BloomStrengthSlider, 0.1f, 1f);
        Control.AddOptionCheckBox(ScpCCVars.GrainToggleOverlay, GrainCheckBox);
    }
}
