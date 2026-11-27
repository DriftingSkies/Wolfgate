using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.PdaUi;

/// <summary>
/// Applies the PDA style classes to a cartridge fragment's stock controls.
/// Classes are used instead of per-instance style boxes so hover and pressed states stay live.
/// </summary>
public static class PdaAppTheme
{
    public const string ButtonClass = "PdaAppButton";
    public const string InputClass = "PdaAppInput";

    /// <summary>Tags every standard button and text field under <paramref name="root"/>.</summary>
    public static void Apply(Control root)
    {
        switch (root)
        {
            case Button button:
                button.AddStyleClass(ButtonClass);
                break;
            case LineEdit edit:
                edit.AddStyleClass(InputClass);
                break;
        }

        foreach (var child in root.Children)
        {
            Apply(child);
        }
    }
}
