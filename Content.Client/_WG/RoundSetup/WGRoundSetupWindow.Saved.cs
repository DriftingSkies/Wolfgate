using System.Linq;
using Content.Shared._WG.RoundSetup;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.RoundSetup;

public sealed partial class WGRoundSetupWindow
{
    private string _personalTitle = string.Empty;
    private bool _personalTitleDirty;
    private LobbyPage? _pendingPage;
    private string _saveName = string.Empty;
    private int? _selectedSavedId;

    /// <summary>Edits the host's displayed title without changing the underlying job.</summary>
    private void BuildPersonalTitle(WGRoundSetupLobby lobby, WGRoundSetupMember member)
    {
        if (lobby.Scenario == WGRoundScenario.FactionShip)
        {
            _personalTitle = string.Empty;
            _personalTitleDirty = false;
            _body.AddChild(Description("faction-titles-fixed"));
            return;
        }
        if (!_personalTitleDirty)
            _personalTitle = member.Title;
        var title = new LineEdit
        {
            Name = "RoundSetupPersonalTitle", Text = _personalTitle, HorizontalExpand = true,
            PlaceHolder = Text("personal-title-placeholder"), Editable = !member.Ready,
            ToolTip = Text(member.Ready ? "unready-edit" : "personal-title-help"),
            IsValid = value => WGRoundSetupPolicy.TryNormalizeRoleName(value, out _),
        };
        title.OnTextChanged += _ => { _personalTitle = title.Text; _personalTitleDirty = true; };
        _body.AddChild(Heading("personal-title"));
        _body.AddChild(title);
    }

    /// <summary>Loads reusable choices into a new crew without restoring membership or consent.</summary>
    private void BuildSavedSetups(WGRoundSetupState state)
    {
        _body.AddChild(Button("back-create", OpenCreate));
        _body.AddChild(Heading("saved-setups"));
        _body.AddChild(Description("saved-help"));
        var selected = state.SavedSetups.FirstOrDefault(saved => saved.Id == _selectedSavedId);
        if (selected == null)
        {
            _selectedSavedId = null;
            if (state.SavedSetups.Length == 0)
                _body.AddChild(Description("saved-empty-help"));
            foreach (var saved in state.SavedSetups.OrderBy(entry => entry.Name))
            {
                var row = new Button { Name = "RoundSetupSavedRow" + saved.Id, HorizontalExpand = true, MinHeight = 40,
                    ToolTip = Text("saved-open-help") };
                row.Label.Visible = false;
                var contents = Row(WGRoundSetupCrewRow.JobIcon(saved.Job),
                    new Label { Text = saved.Name, ClipText = true, HorizontalExpand = true },
                    new Label { Text = ScenarioName(saved.Scenario) });
                contents.MouseFilter = MouseFilterMode.Ignore;
                foreach (var child in contents.Children)
                    child.MouseFilter = MouseFilterMode.Ignore;
                row.AddChild(contents);
                row.OnPressed += _ => { _selectedSavedId = saved.Id; _pageScroll.VScroll = 0; Redraw(); };
                _body.AddChild(row);
            }
            return;
        }
        _body.AddChild(Button("back-saved", () => { _selectedSavedId = null; _pageScroll.VScroll = 0; Redraw(); }));
        _body.AddChild(PlainDescription(selected.Name));
        if (selected.Title.Length > 0)
            _body.AddChild(PlainDescription(selected.Title));
        if (selected.Description.Length > 0)
            _body.AddChild(PlainDescription(selected.Description));
        _body.AddChild(PlainDescription(Text("saved-summary", ("scenario", ScenarioName(selected.Scenario)),
            ("count", selected.Roles.Sum(role => role.Capacity)))));
        if (WGRoundSetupPolicy.IsShip(selected.Scenario))
        {
            _body.AddChild(VesselSummary(state, new WGRoundSetupLobby { Vessel = selected.Vessel, ShipName = selected.ShipName }));
            _body.AddChild(Description(ShipStartKey(selected.ShipStart)));
        }
        _body.AddChild(PlainDescription(Text("saved-personal-job", ("job", JobName(selected.Job)), ("title", selected.JobTitle))));
        foreach (var role in selected.Roles)
            _body.AddChild(Row(WGRoundSetupCrewRow.JobIcon(role.Job), PlainDescription(Text("saved-role",
                ("role", role.Name.Length == 0 ? JobName(role.Job) : role.Name), ("job", JobName(role.Job)), ("count", role.Capacity)))));
        var delete = new ConfirmButton
        {
            Text = Text("delete-setup"), ConfirmationText = Text("delete-confirm"), MinHeight = 40, HorizontalExpand = true,
            ToolTip = Text("delete-saved-confirm", ("name", selected.Name)),
        };
        delete.OnPressed += _ => SendRequest(new WGRoundSetupRequest(WGRoundSetupAction.DeleteSetup) { SavedId = selected.Id });
        _footer.AddChild(Row(Button("load-setup", () => SendRequest(new WGRoundSetupRequest(WGRoundSetupAction.LoadSetup)
        {
            SavedId = selected.Id,
        }), !state.CanJoin, "disabled-join"), delete));
    }

    /// <summary>Saves committed job and ship choices together with the current admission form.</summary>
    private void BuildSaveSetup(WGRoundSetupLobby lobby, WGRoundSetupState state)
    {
        var content = Column(6);
        content.AddChild(Heading("save-setup"));
        var name = new LineEdit
        {
            Name = "RoundSetupSaveName", Text = _saveName, HorizontalExpand = true,
            PlaceHolder = Text("save-name-placeholder"),
            IsValid = value => WGRoundSetupPolicy.TryNormalizeTitle(value, out _),
        };
        var save = Button("save-setup", () =>
        {
            SendRequest(new WGRoundSetupRequest(WGRoundSetupAction.SaveSetup)
            {
                LobbyId = lobby.Id, SaveName = name.Text, Title = _draftTitle, Description = _draftDescription,
                Access = _draftAccess, AllowLateJoin = _draftLateJoin,
            });
        });
        save.Name = "RoundSetupSave";
        void UpdateSave()
        {
            var exists = state.SavedSetups.Any(entry => entry.Name.Equals(name.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            var limit = state.SavedSetups.Length >= WGRoundSetupPolicy.MaximumSavedSetups && !exists;
            if (!WGRoundSetupPolicy.TryNormalizeDescription(_draftDescription, out _))
            {
                save.Disabled = true;
                save.ToolTip = Text("error-description", ("max", WGRoundSetupPolicy.MaximumDescriptionLength));
                return;
            }
            save.Disabled = string.IsNullOrWhiteSpace(name.Text) || limit;
            save.ToolTip = Text(limit ? "save-limit" : string.IsNullOrWhiteSpace(name.Text) ? "save-needs-name" : exists ? "save-replace-help" : "save-help");
        }
        name.OnTextChanged += _ => { _saveName = name.Text; UpdateSave(); };
        _descriptionChecks.Add(UpdateSave);
        UpdateSave();
        content.AddChild(Row(name, save));
        _body.AddChild(Panel(content));
    }

    private static bool SameSavedSetups(WGSavedSetup[] first, WGSavedSetup[] second) =>
        first.Length == second.Length && first.Zip(second).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name && pair.First.Title == pair.Second.Title && pair.First.Description == pair.Second.Description &&
            pair.First.Scenario == pair.Second.Scenario && pair.First.Faction == pair.Second.Faction && pair.First.ShipName == pair.Second.ShipName && pair.First.ShipStart == pair.Second.ShipStart &&
            pair.First.Vessel == pair.Second.Vessel && pair.First.Access == pair.Second.Access &&
            pair.First.AllowLateJoin == pair.Second.AllowLateJoin && pair.First.Job == pair.Second.Job &&
            pair.First.JobTitle == pair.Second.JobTitle && pair.First.Roles.SequenceEqual(pair.Second.Roles));
}
