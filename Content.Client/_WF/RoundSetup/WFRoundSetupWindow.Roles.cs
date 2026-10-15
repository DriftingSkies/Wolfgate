using System.Linq;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private readonly Dictionary<int, int?> _browsedRoles = new();
    private readonly Dictionary<int, RoleDraft> _roleDrafts = new();
    private int? _editingRole;

    /// <summary>Preserves host input while unrelated lobby snapshots arrive.</summary>
    private sealed class RoleDraft
    {
        public string Name = string.Empty;
        public WFRoundSetupJob? Job;
        public int Capacity = 1;
        public bool Dirty;
    }

    private string RoleName(WFRoundSetupRole role) => string.IsNullOrEmpty(role.Name) ? JobName(role.Job) : role.Name;

    private string RoleChoice(WFRoundSetupRole role) => Text(string.IsNullOrEmpty(role.Name) || role.Name == JobName(role.Job) ? "role-choice" : "role-choice-job", ("role", RoleName(role)),
        ("job", JobName(role.Job)), ("open", Math.Max(0, role.Capacity - role.Occupied)), ("capacity", role.Capacity));

    private string MemberRoleName(WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        var role = lobby.Roles.FirstOrDefault(choice => choice.Id == member.RoleId);
        return role == null ? JobName(member.Job) : RoleName(role);
    }

    /// <summary>Chooses the host's own job independently of advertised guest openings.</summary>
    private void BuildPersonalJobSetup(WFRoundSetupState state, WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        if (state.Jobs.Length == 0)
        {
            _body.AddChild(Description("no-jobs"));
            return;
        }
        var jobs = state.Jobs;
        var selected = Array.FindIndex(jobs, job => job.Id == member.Job && job.Station == member.Station);
        var picker = new WFRoundSetupOptionButton
        {
            Name = "RoundSetupPersonalJob",
            HorizontalExpand = true,
            Disabled = member.Ready,
            Filterable = true,
        };
        picker.AddItem(Text("job-unselected"), -1);
        picker.SetItemDisabled(0, true);
        for (var i = 0; i < jobs.Length; i++)
            picker.AddItem(JobChoice(jobs[i]), i);
        picker.SelectId(selected);
        picker.OnItemSelected += args =>
        {
            picker.SelectId(args.Id);
            if (args.Id < 0)
                return;
            var job = jobs[args.Id];
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
            {
                LobbyId = lobby.Id,
                Job = job.Id,
                Station = job.Station,
            });
        };
        _body.AddChild(Heading("personal-job"));
        _body.AddChild(picker);
    }

    /// <summary>Lets the host occupy an advertised captain or specialist slot without privileged job substitution.</summary>
    private void BuildHostRoleChoice(WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        if (lobby.Roles.Length == 0)
            return;
        var selector = new WFRoundSetupOptionButton { Name = "RoundSetupHostRole", HorizontalExpand = true, Disabled = member.Ready };
        selector.AddItem(Text("host-independent-job"), -1);
        foreach (var role in lobby.Roles.Where(role => role.CanSelect || role.Id == member.RoleId))
            selector.AddItem(RoleChoice(role), role.Id);
        selector.SelectId(member.RoleId ?? -1);
        selector.OnItemSelected += args => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectRole)
        {
            LobbyId = lobby.Id, RoleId = args.Id < 0 ? null : args.Id,
        });
        _body.AddChild(Heading("host-position"));
        _body.AddChild(selector);
    }

    private string JobChoice(WFRoundSetupJob job) => string.IsNullOrEmpty(job.StationName)
        ? JobName(job.Id) : Text("job-station", ("job", JobName(job.Id)), ("station", job.StationName));

    /// <summary>Shows advertised slots and remembers the visitor's requested role.</summary>
    private bool BuildBrowseRoles(BoxContainer content, WFRoundSetupLobby lobby, bool available)
    {
        content.AddChild(Heading("roles"));
        foreach (var role in lobby.Roles)
            content.AddChild(Row(WFRoundSetupCrewRow.JobIcon(role.Job), PlainDescription(RoleChoice(role))));
        var choices = lobby.Roles.Where(role => role.CanSelect).ToArray();
        if (choices.Length == 0)
        {
            content.AddChild(Description("no-roles"));
            _browsedRoles.Remove(lobby.Id);
            return false;
        }
        if (!available)
            return true;
        var selector = new WFRoundSetupOptionButton { HorizontalExpand = true };
        foreach (var role in choices)
            selector.AddItem(RoleChoice(role), role.Id);
        var selected = _browsedRoles.GetValueOrDefault(lobby.Id) ?? choices[0].Id;
        if (!choices.Any(role => role.Id == selected))
            selected = choices[0].Id;
        _browsedRoles[lobby.Id] = selected;
        selector.SelectId(selected);
        selector.OnItemSelected += args =>
        {
            selector.SelectId(args.Id);
            _browsedRoles[lobby.Id] = args.Id;
        };
        content.AddChild(selector);
        return true;
    }

    private void JoinLobby(WFRoundSetupAction action, int lobby)
    {
        SendRequest(new WFRoundSetupRequest(action) { LobbyId = lobby, RoleId = _browsedRoles.GetValueOrDefault(lobby) });
    }

    /// <summary>Selects a reserved crew role without exposing jobs the host has not offered.</summary>
    private void BuildMemberRoles(WFRoundSetupLobby lobby, WFRoundSetupMember member)
    {
        var choices = lobby.Roles.Where(role => role.CanSelect || role.Id == member.RoleId).ToArray();
        if (choices.Length == 0)
        {
            _body.AddChild(Description("no-roles"));
            return;
        }
        var selector = new WFRoundSetupOptionButton { HorizontalExpand = true, Disabled = member.Ready };
        foreach (var role in choices)
            selector.AddItem(RoleChoice(role), role.Id);
        selector.SelectId(member.RoleId ?? choices[0].Id);
        selector.OnItemSelected += args =>
        {
            selector.SelectId(args.Id);
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectRole)
            {
                LobbyId = lobby.Id,
                RoleId = args.Id,
            });
        };
        _body.AddChild(Heading("roles"));
        _body.AddChild(selector);
    }

    /// <summary>Lets hosts name openings, choose their underlying job and set reserved slot capacity.</summary>
    private void BuildRoleEditor(WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        _body.AddChild(Heading("roles"));
        _body.AddChild(Description(lobby.Roles.Length == 0 ? "roles-empty-host" : "roles-help"));
        if (lobby.Scenario == WFRoundScenario.Ship)
        {
            _body.AddChild(Description("ship-roster-help"));
            _body.AddChild(FlowRow(Enum.GetValues<WFCrewRoster>().Select(roster =>
                Button("roster-" + roster.ToString().ToLowerInvariant(), () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.ApplyRoster)
                {
                    LobbyId = lobby.Id, Roster = roster,
                }))).ToArray()));
        }
        var canAdd = lobby.Roles.Length < WFRoundSetupPolicy.MaximumRoles && state.RoleJobs.Length > 0;
        var selected = lobby.Roles.FirstOrDefault(role => role.Id == _editingRole);
        if (selected != null || _editingRole == 0 && canAdd)
        {
            _body.AddChild(RoleEditor(state, lobby, selected));
            _body.AddChild(Button("role-cancel", () =>
            {
                _roleDrafts.Remove(_editingRole ?? 0);
                _editingRole = null;
                Redraw();
            }));
            return;
        }
        foreach (var role in lobby.Roles)
        {
            var entry = new Button { HorizontalExpand = true, MinHeight = 40, ToolTip = Text("role-edit") };
            entry.Label.Visible = false;
            var content = Row(WFRoundSetupCrewRow.JobIcon(role.Job), PlainDescription(RoleChoice(role)));
            content.MouseFilter = MouseFilterMode.Ignore;
            foreach (var child in content.Children)
                child.MouseFilter = MouseFilterMode.Ignore;
            entry.AddChild(content);
            entry.OnPressed += _ => { _editingRole = role.Id; Redraw(); };
            _body.AddChild(entry);
        }
        _body.AddChild(Button("role-new", () => { _editingRole = 0; Redraw(); }, !canAdd));
    }

    private PanelContainer RoleEditor(WFRoundSetupState state, WFRoundSetupLobby lobby, WFRoundSetupRole? role)
    {
        var draftId = role?.Id ?? 0;
        if (!_roleDrafts.TryGetValue(draftId, out var draft) || !draft.Dirty)
        {
            draft = new RoleDraft
            {
                Name = role?.Name ?? string.Empty,
                Capacity = role?.Capacity ?? 1,
                Job = role == null ? state.RoleJobs.FirstOrDefault() :
                    state.RoleJobs.FirstOrDefault(job => job.Id == role.Job && job.Station == role.Station)
                    ?? new WFRoundSetupJob(role.Job, role.Station, string.Empty),
            };
            _roleDrafts[draftId] = draft;
        }
        var occupied = role?.Occupied ?? 0;
        var spawned = role != null && lobby.Members.Any(member => member.RoleId == role.Id && member.Spawned);
        var content = Column(5);
        if (role != null)
            content.AddChild(PlainDescription(RoleChoice(role)));
        var name = new LineEdit
        {
            Text = draft.Name,
            PlaceHolder = Text("role-name-placeholder"),
            HorizontalExpand = true,
            Editable = !spawned,
            IsValid = value => value.Length <= WFRoundSetupPolicy.MaximumRoleNameLength,
        };
        name.OnTextChanged += _ => { draft.Name = name.Text; draft.Dirty = true; };
        content.AddChild(Heading("role-name"));
        content.AddChild(name);
        var jobs = new List<WFRoundSetupJob>(state.RoleJobs);
        if (draft.Job is { } current && !jobs.Contains(current))
            jobs.Add(current);
        var selector = new WFRoundSetupOptionButton { HorizontalExpand = true, Filterable = true, Disabled = occupied > 0 || jobs.Count == 0 };
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            selector.AddItem(string.IsNullOrEmpty(job.StationName) ? JobName(job.Id) :
                Text("job-station", ("job", JobName(job.Id)), ("station", job.StationName)), i);
        }
        if (jobs.Count > 0)
        {
            selector.SelectId(Math.Max(0, jobs.FindIndex(job => job == draft.Job)));
            draft.Job = jobs[selector.SelectedId];
        }
        selector.OnItemSelected += args =>
        {
            selector.SelectId(args.Id);
            draft.Job = jobs[args.Id];
            draft.Dirty = true;
        };
        content.AddChild(Heading("role-job"));
        content.AddChild(selector);
        var capacity = new SpinBox
        {
            IsValid = value => value >= Math.Max(1, occupied) && value <= WFRoundSetupPolicy.MaximumMembers,
            Value = Math.Max(Math.Max(1, occupied), draft.Capacity),
        };
        capacity.InitDefaultButtons();
        capacity.ValueChanged += args => { draft.Capacity = args.Value; draft.Dirty = true; };
        var save = Button(role == null ? "role-add" : "role-save", () =>
        {
            if (draft.Job is not { } job)
                return;
            _roleDrafts.Remove(draftId);
            _editingRole = null;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
            {
                LobbyId = lobby.Id,
                RoleId = role?.Id,
                RoleName = name.Text,
                Job = job.Id,
                Station = job.Station,
                Capacity = capacity.Value,
            });
        }, jobs.Count == 0);
        var actions = FlowRow(new Label { Text = Text("role-capacity") }, capacity, save);
        if (role != null)
            actions.AddChild(Button("role-remove", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.RemoveRole)
            {
                LobbyId = lobby.Id,
                RoleId = role.Id,
            }), occupied > 0));
        content.AddChild(actions);
        if (occupied > 0)
            content.AddChild(Description(spawned ? "role-spawned-help" : "role-occupied-help"));
        return Panel(content);
    }
}
