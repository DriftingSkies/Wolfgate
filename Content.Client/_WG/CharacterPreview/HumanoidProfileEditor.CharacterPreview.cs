using Content.Client._WG.CharacterPreview;
using Content.Shared._WG.CharacterPreview;
using Robust.Client.Utility;
using Robust.Shared.Configuration;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    /// <summary>Hooks the preview frame's drag-to-turn and the floor tile button.</summary>
    private void InitializePreviewFrame()
    {
        var cfg = IoCManager.Resolve<IConfigurationManager>();

        // Dragging right turns the same way as the ▶ button.
        PreviewFrame.Turned += step =>
        {
            _previewRotation = step > 0 ? _previewRotation.TurnCcw() : _previewRotation.TurnCw();
            SetPreviewRotation(_previewRotation);
        };

        var background = cfg.GetCVar(CharacterPreviewCVars.Background);
        ApplyPreviewBackground(background);
        PreviewBackgroundButton.OnPressed += _ =>
        {
            var next = (cfg.GetCVar(CharacterPreviewCVars.Background) + 1) % (WGPreviewFrame.Backgrounds.Length + 1);
            cfg.SetCVar(CharacterPreviewCVars.Background, next);
            cfg.SaveToFile();
            ApplyPreviewBackground(next);
        };
    }

    private void ApplyPreviewBackground(int index)
    {
        PreviewFrame.SetBackground(index);
        PreviewBackgroundButton.Text = Loc.GetString("wg-preview-background",
            ("tile", Loc.GetString($"wg-preview-background-{Math.Clamp(index, 0, WGPreviewFrame.Backgrounds.Length)}")));
    }
}
