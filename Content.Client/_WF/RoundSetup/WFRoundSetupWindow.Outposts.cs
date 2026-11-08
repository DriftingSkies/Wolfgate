using Content.Client._WF.Outposts;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    /// <summary>The Outpost crew's landed outpost, its chosen save, or what it is waiting for.</summary>
    private RichTextLabel OutpostSummary(WFRoundSetupLobby lobby) =>
        lobby.Started ? PlainDescription(Text("outpost-landed", ("name", lobby.OutpostName)))
        : lobby.OutpostLanding ? Description("outpost-landing-status")
        : lobby.OutpostSlot == null && lobby.OutpostPreset == null ? Description("outpost-waiting-host")
        : PlainDescription(Text("outpost-chosen", ("name", lobby.OutpostName)));

    /// <summary>The Your outpost page: the host picks one of their saves, with its preview; members see the pick.</summary>
    private void BuildOutpostSettings(WFRoundSetupLobby lobby, bool host)
    {
        _body.AddChild(Heading("outpost-tab"));
        _body.AddChild(OutpostSummary(lobby));
        if (!host || lobby.OutpostLanding)
            return;

        _body.AddChild(new WFOutpostSavesPanel(slot => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectOutpost)
        {
            LobbyId = lobby.Id,
            OutpostSlot = slot,
        }), lobby.OutpostSlot, "wf-round-setup-outpost-saves", preset => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectOutpost)
        {
            LobbyId = lobby.Id,
            OutpostPreset = preset,
        }), lobby.OutpostPreset) { VerticalExpand = true, Name = "RoundSetupOutpostSaves" });
    }
}
