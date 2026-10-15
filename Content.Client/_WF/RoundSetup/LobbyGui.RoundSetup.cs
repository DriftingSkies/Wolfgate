using System.Linq;
using Content.Client._WF.RoundSetup;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyGui
{
    /// <summary>Fits the character card and labels the communication tabs without duplicating setup actions.</summary>
    private void InitializeRoundSetupHome()
    {
        SetAnchorPreset(HomeBackdrop, LayoutPreset.Wide);
        CharacterPreview.UseRoundSetupCard();
        LobbyInformationTabs.SetTabTitle(0, Loc.GetString("wf-round-setup-home-chat"));
        LobbyInformationTabs.SetTabTitle(1, Loc.GetString("wf-round-setup-home-briefing"));
        VoteContainer.OnChildAdded += _ =>
        {
            LobbyInformationTabs.SetTabTitle(1, Loc.GetString("wf-round-setup-home-briefing-vote"));
            LobbyInformationTabs.CurrentTab = 1;
        };
        VoteContainer.OnChildRemoved += _ =>
        {
            if (VoteContainer.ChildCount != 0 || LobbyInformationTabs.ChildCount < 2)
                return;
            LobbyInformationTabs.SetTabTitle(1, Loc.GetString("wf-round-setup-home-briefing"));
            LobbyInformationTabs.CurrentTab = 0;
        };
        foreach (var button in new[] { ReadyButton, RoundSetupButton, CustomizeButton })
            WrapHomeButton(button);
    }

    /// <summary>Updates readiness copy together with the primary action.</summary>
    public void SetRoundSetupJoinText(string caption, string help)
    {
        ReadyButton.Text = caption;
        var text = ReadyButton.Children.OfType<RichTextLabel>().Single();
        text.SetMessage(FormattedMessage.FromUnformatted(caption));
        NormalJoinHelp.SetMessage(FormattedMessage.FromUnformatted(help));
    }

    /// <summary>Clears crew readiness before editing the character in the main lobby.</summary>
    private void ClearRoundSetupReadiness()
    {
        var setup = IoCManager.Resolve<IEntityManager>().System<WFRoundSetupClientSystem>();
        if (setup.State?.OwnLobby is { } lobby)
            setup.Send(new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobby, Ready = false });
    }

    private static void WrapHomeButton(Button button)
    {
        button.Label.Visible = false;
        var caption = new RichTextLabel { HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore };
        caption.SetMessage(FormattedMessage.FromUnformatted(button.Text ?? string.Empty));
        button.AddChild(caption);
    }
}
