using Content.Client._WF.RoundSetup;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyGui
{
    /// <summary>Fits character preparation and centers its primary actions.</summary>
    private void InitializeRoundSetupHome()
    {
        SetAnchorPreset(HomeBackdrop, LayoutPreset.Wide);
        CharacterPreview.UseRoundSetupCard();
        foreach (var button in new[] { ReadyButton, RoundSetupButton, CustomizeButton })
        {
            button.Label.Align = Label.AlignMode.Center;
            button.Label.ClipText = false;
        }
    }

    /// <summary>Updates readiness copy together with the primary action.</summary>
    public void SetRoundSetupJoinText(string caption, string help)
    {
        ReadyButton.Text = caption;
        NormalJoinHelp.SetMessage(FormattedMessage.FromUnformatted(help));
    }

    /// <summary>Clears crew readiness before editing the character in the main lobby.</summary>
    private void ClearRoundSetupReadiness()
    {
        var setup = IoCManager.Resolve<IEntityManager>().System<WFRoundSetupClientSystem>();
        if (setup.State?.OwnLobby is { } lobby)
            setup.Send(new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobby, Ready = false });
    }
}
