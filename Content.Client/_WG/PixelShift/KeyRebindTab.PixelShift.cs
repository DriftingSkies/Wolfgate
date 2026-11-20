using Content.Shared._WG.PixelShift;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Input;

namespace Content.Client.Options.UI.Tabs;

/// <summary>Speed entries beside the pixel shift and tilt keys on the Controls tab.</summary>
public sealed partial class KeyRebindTab
{
    /// <summary>Most digits a speed entry takes.</summary>
    private const int WgSpeedDigits = 3;

    /// <summary>Puts a speed entry on the pixel shift and tilt key rows, saved to the player's own cvars.</summary>
    private void WgAddPixelShiftSpeeds()
    {
        WgAddSpeedEntry(WGPixelShiftKeys.WGPixelShift,
            WGPixelShiftCVars.ShiftSpeed,
            WGPixelShiftSystem.MinShiftSpeed,
            WGPixelShiftSystem.MaxShiftSpeed,
            "ui-options-wg-pixel-shift-speed-unit",
            "ui-options-wg-pixel-shift-speed-tooltip");
        WgAddSpeedEntry(WGPixelShiftKeys.WGPixelTilt,
            WGPixelShiftCVars.TiltSpeed,
            WGPixelShiftSystem.MinTiltSpeed,
            WGPixelShiftSystem.MaxTiltSpeed,
            "ui-options-wg-pixel-tilt-speed-unit",
            "ui-options-wg-pixel-tilt-speed-tooltip");
    }

    private void WgAddSpeedEntry(BoundKeyFunction function, CVarDef<int> cvar, int min, int max, string unit, string tooltip)
    {
        // A key row holds one horizontal box: spacer, name, two bind buttons, spacer, reset.
        if (!_keyControls.TryGetValue(function, out var row) || row.ChildCount == 0 || row.GetChild(0) is not BoxContainer box)
            return;

        var tip = Loc.GetString(tooltip, ("min", min), ("max", max));
        var edit = new LineEdit
        {
            MinWidth = 48,
            Text = Math.Clamp(_cfg.GetCVar(cvar), min, max).ToString(),
            IsValid = WgIsSpeedText,
            ToolTip = tip,
        };

        void Commit(LineEdit.LineEditEventArgs args)
        {
            var current = _cfg.GetCVar(cvar);
            var value = int.TryParse(args.Text, out var parsed) ? Math.Clamp(parsed, min, max) : Math.Clamp(current, min, max);
            edit.Text = value.ToString();
            if (value == current)
                return;

            _cfg.SetCVar(cvar, value);
            _cfg.SaveToFile();
        }

        edit.OnTextEntered += Commit;
        edit.OnFocusExit += Commit;

        var entry = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Children =
            {
                edit,
                new Label
                {
                    Text = Loc.GetString(unit),
                    ToolTip = tip,
                    MouseFilter = MouseFilterMode.Stop,
                    Margin = new Thickness(4, 0, 0, 0),
                },
            },
        };

        box.AddChild(entry);
        entry.SetPositionInParent(2); // Just after the key's name.
    }

    private static bool WgIsSpeedText(string text)
    {
        if (text.Length > WgSpeedDigits)
            return false;

        foreach (var c in text)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }
}
