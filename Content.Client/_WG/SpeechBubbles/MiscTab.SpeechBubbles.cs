using Content.Client._WG.SpeechBubbles;
using Content.Shared._WG.SpeechBubbles;

namespace Content.Client.Options.UI.Tabs;

/// <summary>The speech bubble toggles and length slider on the Misc tab.</summary>
public sealed partial class MiscTab
{
    private void WgAddSpeechBubbleOptions()
    {
        Control.AddOptionCheckBox(SpeechBubbleCVars.Enabled, SpeechBubblesCheckBox);
        Control.AddOptionCheckBox(SpeechBubbleCVars.NonMobs, SpeechBubblesNonMobsCheckBox);
        Control.AddOptionCheckBox(SpeechBubbleCVars.Ghosts, SpeechBubblesGhostsCheckBox);
        Control.AddOptionCheckBox(SpeechBubbleCVars.Emotes, SpeechBubblesEmotesCheckBox);
        Control.AddOptionSlider(
            SpeechBubbleCVars.MaxLength,
            SpeechBubbleMaxLengthSlider,
            0,
            300,
            (_, value) => value == 0
                ? Loc.GetString("ui-options-wg-speech-bubble-max-length-off")
                : Loc.GetString("ui-options-wg-speech-bubble-max-length-value",
                    ("length", Math.Max(value, WGBubbleLength.MinLength))));
    }
}
