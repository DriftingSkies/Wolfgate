using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Feedback;

public sealed partial class FeedbackUIController
{
    private MenuButton? FeedbackButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.FeedbackButton;

    /// <summary>Hooks up the green bug report button on the top menu bar.</summary>
    public void LoadButton()
    {
        if (FeedbackButton is not { } button)
            return;

        button.AddStyleClass(StyleNano.StyleClassButtonColorGreen);
        button.OnPressed += MenuButtonPressed;
    }

    public void UnloadButton()
    {
        if (FeedbackButton is { } button)
            button.OnPressed -= MenuButtonPressed;
    }

    private void MenuButtonPressed(BaseButton.ButtonEventArgs args)
    {
        ToggleWindow();
    }
}
