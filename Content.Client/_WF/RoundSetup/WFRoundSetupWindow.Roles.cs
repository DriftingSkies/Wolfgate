using System.Linq;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private readonly Dictionary<int, int?> _browsedRoles = new();
    private readonly Dictionary<int, RoleDraft> _roleDrafts = new();
    private int? _editingRole;
    private WFRoundSetupJob? _selectedPersonalJob;

    /// <summary>Preserves host input while unrelated lobby snapshots arrive.</summary>
    private sealed class RoleDraft
    {
        public string Name = string.Empty;
        public WFRoundSetupJob? Job;
        public int Capacity = 1;
        public bool Dirty;
    }

    private string RoleName(WFRoundSetupRole role) => string.IsNullOrEmpty(role.Name) ? JobName(role.Job) : role.Name;

    private string RoleChoice(WFRoundSetupRole role) => Text("role-choice", ("role", RoleName(role)),
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
        var selected = Array.FindIndex(jobs, job => job == _selectedPersonalJob);
        if (selected < 0)
            selected = Math.Max(0, Array.FindIndex(jobs, job => job.Id == member.Job && job.Station == member.Station));
        var picker = new OptionButton
        {
            Name = "RoundSetupPersonalJob",
            HorizontalExpand = true,
            MaxWidth = ChoiceWidth,
            Disabled = member.Ready,
            Filterable = true,
        };
        for (var i = 0; i < jobs.Length; i++)
            picker.AddItem(JobChoice(jobs[i]), i);
        picker.SelectId(selected);
        var caption = PlainDescription(JobChoice(jobs[selected]));
        picker.OnItemSelected += args =>
        {
            picker.SelectId(args.Id);
            _selectedPersonalJob = jobs[args.Id];
            caption.SetMessage(Robust.Shared.Utility.FormattedMessage.FromUnformatted(JobChoice(jobs[args.Id])));
        };
        var select = Button("select-job", () =>
        {
            var job = jobs[picker.SelectedId];
            _selectedPersonalJob = null;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
            {
                LobbyId = lobby.Id,
                Job = job.Id,
                Station = job.Station,
            });
        }, member.Ready);
        _body.AddChild(Heading("personal-job"));
        _body.AddChild(FlowRow(picker, select));
        _body.AddChild(caption);
    }

    private string JobChoice(WFRoundSetupJob job) => string.IsNullOrEmpty(job.StationName)
        ? JobName(job.Id) : Text("job-station", ("job", JobName(job.Id)), ("station", job.StationName));

    /// <summary>Shows advertised slots and remembers the visitor's requested role.</summary>
    private bool BuildBrowseRoles(BoxContainer content, WFRoundSetupLobby lobby, bool available)
    {
        var openings = lobby.Roles.Select(role => Text("role-opening", ("role", RoleName(role)),
            ("open", Math.Max(0, role.Capacity - role.Occupied))));
        content.AddChild(PlainDescription(Text("role-openings", ("roles", string.Join(" · ", openings)))));
        var choices = lobby.Roles.Where(role => role.CanSelect).ToArray();
        if (choices.Length == 0)
        {
            content.AddChild(Description("no-roles"));
            _browsedRoles.Remove(lobby.Id);
            return false;
        }
        if (!available)
            return true;
        var selector = new OptionButton { HorizontalExpand = true, MaxWidth = ChoiceWidth, RectClipContent = true };
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
        var selector = new OptionButton { HorizontalExpand = true, MaxWidth = ChoiceWidth, RectClipContent = true, Disabled = member.Ready };
        foreach (var role in choices)
            selector.AddItem(RoleChoice(role), role.Id);
        var selected = choices.FirstOrDefault(role => role.Id == _selectedRole)
            ?? choices.FirstOrDefault(role => role.Id == member.RoleId) ?? choices[0];
        selector.SelectId(selected.Id);
        var select = Button("select-role", () =>
        {
            _selectedRole = null;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectRole)
            {
                LobbyId = lobby.Id,
                RoleId = selector.SelectedId,
            });
        }, member.Ready || !selected.CanSelect);
        selector.OnItemSelected += args =>
        {
            selector.SelectId(args.Id);
            _selectedRole = args.Id;
            select.Disabled = member.Ready || !choices.First(role => role.Id == args.Id).CanSelect;
        };
        _body.AddChild(FlowRow(selector, select));
    }

    /// <summary>Lets hosts name openings, choose their underlying job and set reserved slot capacity.</summary>
    private void BuildRoleEditor(WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        var section = Column(8);
        section.AddChild(Heading("roles"));
        var canAdd = lobby.Roles.Length < WFRoundSetupPolicy.MaximumRoles && state.Jobs.Length > 0;
        var selected = lobby.Roles.FirstOrDefault(role => role.Id == _editingRole);
        var adding = _editingRole == 0 && canAdd;
        if (lobby.Roles.Length == 0 && !adding)
        {
            section.AddChild(Description("roles-empty-host"));
            section.AddChild(Button("role-new", () => { _editingRole = 0; Redraw(); }, !canAdd));
            _body.AddChild(Panel(section));
            return;
        }
        section.AddChild(Description("roles-help"));
        if (!adding && selected == null)
            selected = lobby.Roles.FirstOrDefault();
        var picker = new OptionButton { HorizontalExpand = true, MaxWidth = ChoiceWidth, RectClipContent = true };
        foreach (var role in lobby.Roles)
            picker.AddItem(RoleChoice(role), role.Id);
        if (canAdd)
            picker.AddItem(Text("role-new"), 0);
        if (selected != null || canAdd)
        {
            picker.SelectId(adding || selected == null ? 0 : selected.Id);
            picker.OnItemSelected += args =>
            {
                _editingRole = args.Id;
                Redraw();
            };
            section.AddChild(picker);
            section.AddChild(RoleEditor(state, lobby, adding ? null : selected));
        }
        else
            section.AddChild(Description("no-roles"));
        _body.AddChild(Panel(section));
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
                Job = role == null ? state.Jobs.FirstOrDefault() :
                    state.Jobs.FirstOrDefault(job => job.Id == role.Job && job.Station == role.Station)
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
        content.AddChild(name);
        var jobs = new List<WFRoundSetupJob>(state.Jobs);
        if (draft.Job is { } current && !jobs.Contains(current))
            jobs.Add(current);
        var selector = new OptionButton { HorizontalExpand = true, MaxWidth = ChoiceWidth, RectClipContent = true, Disabled = occupied > 0 || jobs.Count == 0 };
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
        content.AddChild(Row(new Label { Text = Text("role-job") }, selector));
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
