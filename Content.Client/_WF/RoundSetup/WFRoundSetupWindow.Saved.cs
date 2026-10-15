using System.Linq;
using Content.Shared._WF.RoundSetup;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private string _personalTitle = string.Empty;
    private bool _personalTitleDirty;
    private LobbyPage? _pendingPage;
    private string _saveName = string.Empty;

    /// <summary>Edits the host's displayed title without changing the underlying job.</summary>
    private void BuildPersonalTitle(WFRoundSetupMember member)
    {
        if (!_personalTitleDirty)
            _personalTitle = member.Title;
        var title = new LineEdit
        {
            Name = "RoundSetupPersonalTitle", Text = _personalTitle, HorizontalExpand = true,
            PlaceHolder = Text("personal-title-placeholder"), Editable = !member.Ready,
            ToolTip = Text(member.Ready ? "unready-edit" : "personal-title-help"),
            IsValid = value => WFRoundSetupPolicy.TryNormalizeRoleName(value, out _),
        };
        title.OnTextChanged += _ => { _personalTitle = title.Text; _personalTitleDirty = true; };
        _body.AddChild(Heading("personal-title"));
        _body.AddChild(title);
    }

    /// <summary>Loads reusable choices into a new crew without restoring membership or consent.</summary>
    private void BuildSavedSetups(WFRoundSetupState state)
    {
        if (state.SavedSetups.Length == 0)
            return;
        _body.AddChild(Heading("saved-setups"));
        _body.AddChild(Description("saved-help"));
        foreach (var saved in state.SavedSetups.OrderBy(entry => entry.Name))
        {
            var row = Column(4);
            row.AddChild(PlainDescription(saved.Name));
            row.AddChild(PlainDescription(Text("saved-summary", ("scenario", ScenarioName(saved.Scenario)),
                ("count", saved.Roles.Sum(role => role.Capacity)))));
            var delete = new ConfirmButton
            {
                Text = Text("delete-setup"), ConfirmationText = Text("delete-confirm"), MinHeight = 40, HorizontalExpand = true,
                ToolTip = Text("delete-saved-confirm", ("name", saved.Name)),
            };
            delete.OnPressed += _ => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.DeleteSetup) { SavedId = saved.Id });
            row.AddChild(Row(Button("load-setup", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.LoadSetup)
            {
                SavedId = saved.Id,
            }), !state.CanJoin, "disabled-join"), delete));
            _body.AddChild(Panel(row));
        }
    }

    /// <summary>Saves committed job and ship choices together with the current admission form.</summary>
    private void BuildSaveSetup(WFRoundSetupLobby lobby, WFRoundSetupState state)
    {
        var content = Column(6);
        content.AddChild(Heading("save-setup"));
        var name = new LineEdit
        {
            Name = "RoundSetupSaveName", Text = _saveName, HorizontalExpand = true,
            PlaceHolder = Text("save-name-placeholder"),
            IsValid = value => WFRoundSetupPolicy.TryNormalizeTitle(value, out _),
        };
        var save = Button("save-setup", () =>
        {
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SaveSetup)
            {
                LobbyId = lobby.Id, SaveName = name.Text, Title = _draftTitle,
                Access = _draftAccess, AllowLateJoin = _draftLateJoin,
            });
        });
        save.Name = "RoundSetupSave";
        void UpdateSave()
        {
            var exists = state.SavedSetups.Any(entry => entry.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            var limit = state.SavedSetups.Length >= WFRoundSetupPolicy.MaximumSavedSetups && !exists;
            save.Disabled = string.IsNullOrWhiteSpace(name.Text) || limit;
            save.ToolTip = Text(limit ? "save-limit" : string.IsNullOrWhiteSpace(name.Text) ? "save-needs-name" : exists ? "save-replace-help" : "save-help");
        }
        name.OnTextChanged += _ => { _saveName = name.Text; UpdateSave(); };
        UpdateSave();
        content.AddChild(Row(name, save));
        _body.AddChild(Panel(content));
    }

    private static bool SameSavedSetups(WFSavedSetup[] first, WFSavedSetup[] second) =>
        first.Length == second.Length && first.Zip(second).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.Title == pair.Second.Title &&
            pair.First.Scenario == pair.Second.Scenario && pair.First.ShipStart == pair.Second.ShipStart &&
            pair.First.Vessel == pair.Second.Vessel && pair.First.Access == pair.Second.Access &&
            pair.First.AllowLateJoin == pair.Second.AllowLateJoin && pair.First.Job == pair.Second.Job &&
            pair.First.JobTitle == pair.Second.JobTitle && pair.First.Roles.SequenceEqual(pair.Second.Roles));
}
