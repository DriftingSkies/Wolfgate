using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>The ship's name, who owns it, and handing it to someone else: transfer, add a co-owner, remove an owner, release.</summary>
public sealed class WGShipAdminOwnershipTab : WGShipAdminTab
{
    private readonly LineEdit _name;
    private readonly Button _rename;
    private readonly BoxContainer _owners;
    private readonly PlayerListControl _players;
    private readonly WGShipAdminText _picked;
    private readonly CheckBox _clearAllowList;
    private readonly CheckBox _keepPrevious;
    private readonly CheckBox _dropCompany;
    private readonly CheckBox _leaveCrewMode;
    private readonly CheckBox _detachHangar;
    private readonly WGShipAdminDropdown _faction;
    private readonly CheckBox _notify;
    private readonly ConfirmButton _transfer;
    private readonly Button _addOwner;
    private readonly ConfirmButton _release;

    private readonly WGShipAdminDropdown _factions;
    private readonly List<string> _factionIds = new();
    private readonly Button _setFaction;
    private readonly WGShipAdminDropdown _companies;
    private readonly List<int?> _companyIds = new();
    private readonly Button _setCompany;

    private PlayerInfo? _player;
    private AdminFlags _flags;

    /// <summary>The ship and affiliation the pickers were last set to, so a refresh doesn't undo a pick in progress.</summary>
    private NetEntity _shown;
    private string? _shownFaction;
    private int? _shownCompany;
    private bool _companyEdited;
    private bool _companyPickGone;

    public override string Title => Loc.GetString("wg-ship-admin-tab-ownership");

    public WGShipAdminOwnershipTab()
    {
        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-ownership-name")));
        _name = new LineEdit { MinWidth = 260 };
        _name.IsValid = text => text.Trim().Length > 0 && text.Trim().Length <= ShuttleDeedComponent.MaxNameLength;
        _rename = new Button { Text = Loc.GetString("wg-ship-admin-ownership-rename"), StyleClasses = { "ButtonSquare" } };
        _rename.OnPressed += _ => Send(new WGShipAdminRenameMessage(Grid, _name.Text));
        Body.AddChild(Row(_name, _rename));

        _owners = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-overview-owners")));
        Body.AddChild(_owners);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-ownership-affiliation")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-ownership-affiliation-note")));
        _factions = Dropdown();
        _factionIds.Add(string.Empty);
        _factions.AddItem(Loc.GetString("wg-ship-admin-none"), 0);
        var protos = IoCManager.Resolve<IPrototypeManager>();
        foreach (var faction in protos.EnumeratePrototypes<CompanyPrototype>()
                     .Where(proto => proto.ID != "None")
                     .OrderBy(proto => Loc.GetString(proto.Name), StringComparer.CurrentCultureIgnoreCase))
        {
            _factions.AddItem(Loc.GetString(faction.Name), _factionIds.Count);
            _factionIds.Add(faction.ID);
        }

        _setFaction = new Button
        {
            Text = Loc.GetString("wg-ship-admin-ownership-set-faction"),
            ToolTip = Loc.GetString("wg-ship-admin-ownership-set-faction-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _setFaction.OnPressed += _ =>
        {
            var picked = _factions.SelectedId;
            if (picked >= 0 && picked < _factionIds.Count)
                Send(new WGShipAdminFactionMessage(Grid, _factionIds[picked]));
        };
        Body.AddChild(Row(_factions, _setFaction));

        _companies = Dropdown();
        _setCompany = new Button
        {
            Text = Loc.GetString("wg-ship-admin-ownership-set-company"),
            ToolTip = Loc.GetString("wg-ship-admin-ownership-set-company-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _setCompany.OnPressed += _ =>
        {
            var picked = _companies.SelectedId;
            if (picked >= 0 && picked < _companyIds.Count)
                Send(new WGShipAdminCompanyMessage(Grid, _companyIds[picked]));
        };
        // A pick by staff is kept through list refreshes; until then the picker follows the ship's company.
        _companies.OnItemSelected += _ =>
        {
            _companyEdited = true;
            _companyPickGone = false;
            GateCompany();
        };
        Body.AddChild(Row(_companies, _setCompany));
        SetCompanies(new List<WGShipAdminCompany>());

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-ownership-give")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-ownership-give-note")));
        _players = new PlayerListControl { MinSize = new Vector2(0, 170) };
        _players.OnSelectionChanged += OnPlayerPicked;
        Body.AddChild(_players);
        _picked = Note(Loc.GetString("wg-ship-admin-ownership-nobody-picked"));
        Body.AddChild(_picked);

        _keepPrevious = Check("wg-ship-admin-ownership-keep-previous", false);
        _clearAllowList = Check("wg-ship-admin-ownership-clear-allow", false);
        _dropCompany = Check("wg-ship-admin-ownership-drop-company", true);
        _leaveCrewMode = Check("wg-ship-admin-ownership-leave-crew", true);
        _detachHangar = Check("wg-ship-admin-ownership-detach-hangar", false);
        _notify = Check("wg-ship-admin-notify", true);

        _faction = Dropdown();
        foreach (var choice in Enum.GetValues<WGShipAdminFaction>())
            _faction.AddItem(Loc.GetString($"wg-ship-admin-ownership-faction-{choice.ToString().ToLowerInvariant()}"), (int) choice);

        Body.AddChild(_faction);

        _transfer = new ConfirmButton
        {
            Text = Loc.GetString("wg-ship-admin-ownership-transfer"),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _transfer.OnPressed += _ => SendTransfer();
        _addOwner = new Button
        {
            Text = Loc.GetString("wg-ship-admin-ownership-add"),
            ToolTip = Loc.GetString("wg-ship-admin-ownership-add-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _addOwner.OnPressed += _ =>
        {
            if (_player != null)
                Send(new WGShipAdminOwnerMessage(Grid, true, _player.SessionId, null, _notify.Pressed));
        };

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        buttons.AddChild(_transfer);
        buttons.AddChild(_addOwner);
        Body.AddChild(buttons);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-ownership-release-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-ownership-release-note")));
        _release = new ConfirmButton
        {
            Text = Loc.GetString("wg-ship-admin-ownership-release"),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
            HorizontalAlignment = HAlignment.Left,
        };
        _release.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Release, notify: _notify.Pressed));
        Body.AddChild(_release);
    }

    /// <summary>How many owners the tab lists, for tests.</summary>
    public int OwnerCount { get; private set; }

    protected override void Show(WGShipAdminDetail detail, AdminFlags flags)
    {
        _flags = flags;
        OwnerCount = detail.Owners.Count;
        _owners.RemoveAllChildren();
        _owners.AddChild(Line(Loc.GetString("wg-ship-admin-overview-record-owner"),
            detail.RecordOwner.Length > 0 ? detail.RecordOwner : Loc.GetString("wg-ship-admin-none")));
        if (detail.Owners.Count == 0)
            _owners.AddChild(Note(Loc.GetString("wg-ship-admin-overview-no-owners")));

        foreach (var owner in detail.Owners)
            _owners.AddChild(OwnerRow(owner, flags, detail.Owners.Count > 1));

        var hangar = (detail.Flags & WGShipAdminRowFlags.Hangar) != 0;
        _detachHangar.Visible = hangar;
        Gate(_detachHangar, WGShipAdminAction.DetachHangar, flags);
        _leaveCrewMode.Visible = (detail.Flags & WGShipAdminRowFlags.CrewLinked) != 0;
        _dropCompany.Visible = (detail.Flags & WGShipAdminRowFlags.Company) != 0;

        Gate(_release, WGShipAdminAction.Release, flags);
        Gate(_setFaction, WGShipAdminAction.SetFaction, flags);
        Gate(_rename, WGShipAdminAction.Rename, flags);

        // The name box is only refilled for another ship, so typing isn't wiped by a refresh.
        if (_shown != detail.Grid)
            _name.Text = detail.Name;

        if (_shown != detail.Grid || _shownFaction != detail.Faction || _shownCompany != detail.CompanyId)
        {
            _shown = detail.Grid;
            _shownFaction = detail.Faction;
            _shownCompany = detail.CompanyId;
            _factions.SelectId(Math.Max(0, _factionIds.IndexOf(detail.Faction)));
            _companyEdited = false;
            _companyPickGone = false;
            _companies.SelectId(Math.Max(0, _companyIds.IndexOf(detail.CompanyId)));
        }

        GateCompany();
        UpdateButtons();
    }

    public override void SetCompanies(List<WGShipAdminCompany> companies)
    {
        var selected = _companies.SelectedId >= 0 && _companies.SelectedId < _companyIds.Count ? _companyIds[_companies.SelectedId] : null;
        _companies.Clear();
        _companyIds.Clear();
        _companyIds.Add(null);
        _companies.AddItem(Loc.GetString("wg-ship-admin-none"), 0);
        foreach (var company in companies)
        {
            var label = company.Tag.Length > 0 ? $"[{company.Tag}] {company.Name}" : company.Name;
            _companies.AddItem(label, _companyIds.Count);
            _companyIds.Add(company.Id);
        }

        // Staff's own pick, or the ship's company, which may only now have arrived in the list.
        var wanted = _companyEdited ? selected : _shownCompany;
        var index = _companyIds.IndexOf(wanted);
        _companies.SelectId(Math.Max(0, index));

        // A company staff picked was disbanded: Set must not quietly send None instead.
        if (_companyEdited && wanted != null && index < 0)
            _companyPickGone = true;

        GateCompany();
    }

    private void GateCompany()
    {
        Gate(_setCompany, WGShipAdminAction.SetCompany, _flags, (_companyIds.Count > 1 || _shownCompany != null) && !_companyPickGone);
        if (_companyPickGone)
            _setCompany.ToolTip = Loc.GetString("wg-ship-admin-ownership-company-gone");
        else if (WGShipAdminPermissions.Allowed(_flags, WGShipAdminAction.SetCompany))
            _setCompany.ToolTip = Loc.GetString("wg-ship-admin-ownership-set-company-tooltip");
    }

    private Control OwnerRow(WGShipAdminOwner owner, AdminFlags flags, bool removable)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(Line(WGShipAdminOverviewTab.OwnerKind(owner.Kind), WGShipAdminOverviewTab.OwnerText(owner), false));

        var remove = new ConfirmButton
        {
            Text = Loc.GetString("wg-ship-admin-ownership-remove"),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
            ToolTip = removable ? null : Loc.GetString("wg-ship-admin-ownership-remove-last"),
        };
        Gate(remove, WGShipAdminAction.RemoveOwner, flags, removable);
        remove.OnPressed += _ => Send(new WGShipAdminOwnerMessage(Grid, false, owner.User, owner.Card, _notify.Pressed));
        row.AddChild(remove);
        return row;
    }

    private void OnPlayerPicked(PlayerInfo? player)
    {
        _player = player;
        _picked.Text = player == null
            ? Loc.GetString("wg-ship-admin-ownership-nobody-picked")
            : Loc.GetString("wg-ship-admin-ownership-picked", ("name", player.CharacterName), ("username", player.Username));
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        Gate(_transfer, WGShipAdminAction.Transfer, _flags, _player != null);
        Gate(_addOwner, WGShipAdminAction.AddOwner, _flags, _player != null);
    }

    private void SendTransfer()
    {
        if (_player == null)
            return;

        Send(new WGShipAdminTransferMessage(Grid, _player.SessionId)
        {
            ClearAllowList = _clearAllowList.Pressed,
            KeepPrevious = _keepPrevious.Pressed,
            DropCompany = _dropCompany.Pressed,
            LeaveCrewMode = _leaveCrewMode.Pressed,
            DetachHangar = _detachHangar.Visible && _detachHangar.Pressed,
            Faction = (WGShipAdminFaction) _faction.SelectedId,
            Notify = _notify.Pressed,
        });
    }

    private CheckBox Check(string key, bool pressed)
    {
        var box = new CheckBox
        {
            Text = Loc.GetString(key),
            ToolTip = Loc.GetString($"{key}-tooltip"),
            Pressed = pressed,
        };
        Body.AddChild(box);
        return box;
    }
}
