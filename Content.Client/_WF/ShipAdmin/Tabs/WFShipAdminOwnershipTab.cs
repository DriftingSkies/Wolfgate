using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ShipAdmin.Tabs;

/// <summary>Who owns the ship, and handing it to someone else: transfer, add a co-owner, remove an owner, release.</summary>
public sealed class WFShipAdminOwnershipTab : WFShipAdminTab
{
    private readonly BoxContainer _owners;
    private readonly PlayerListControl _players;
    private readonly WFShipAdminText _picked;
    private readonly CheckBox _clearAllowList;
    private readonly CheckBox _keepPrevious;
    private readonly CheckBox _dropCompany;
    private readonly CheckBox _leaveCrewMode;
    private readonly CheckBox _detachHangar;
    private readonly WFShipAdminDropdown _faction;
    private readonly CheckBox _notify;
    private readonly ConfirmButton _transfer;
    private readonly Button _addOwner;
    private readonly ConfirmButton _release;

    private PlayerInfo? _player;
    private AdminFlags _flags;

    public override string Title => Loc.GetString("wf-ship-admin-tab-ownership");

    public WFShipAdminOwnershipTab()
    {
        _owners = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-overview-owners")));
        Body.AddChild(_owners);

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-ownership-give")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-ownership-give-note")));
        _players = new PlayerListControl { MinSize = new Vector2(0, 170) };
        _players.OnSelectionChanged += OnPlayerPicked;
        Body.AddChild(_players);
        _picked = Note(Loc.GetString("wf-ship-admin-ownership-nobody-picked"));
        Body.AddChild(_picked);

        _keepPrevious = Check("wf-ship-admin-ownership-keep-previous", false);
        _clearAllowList = Check("wf-ship-admin-ownership-clear-allow", false);
        _dropCompany = Check("wf-ship-admin-ownership-drop-company", true);
        _leaveCrewMode = Check("wf-ship-admin-ownership-leave-crew", true);
        _detachHangar = Check("wf-ship-admin-ownership-detach-hangar", false);
        _notify = Check("wf-ship-admin-notify", true);

        _faction = Dropdown();
        foreach (var choice in Enum.GetValues<WFShipAdminFaction>())
            _faction.AddItem(Loc.GetString($"wf-ship-admin-ownership-faction-{choice.ToString().ToLowerInvariant()}"), (int) choice);

        Body.AddChild(_faction);

        _transfer = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-ownership-transfer"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _transfer.OnPressed += _ => SendTransfer();
        _addOwner = new Button
        {
            Text = Loc.GetString("wf-ship-admin-ownership-add"),
            ToolTip = Loc.GetString("wf-ship-admin-ownership-add-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _addOwner.OnPressed += _ =>
        {
            if (_player != null)
                Send(new WFShipAdminOwnerMessage(Grid, true, _player.SessionId, null, _notify.Pressed));
        };

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        buttons.AddChild(_transfer);
        buttons.AddChild(_addOwner);
        Body.AddChild(buttons);

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-ownership-release-heading")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-ownership-release-note")));
        _release = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-ownership-release"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
            HorizontalAlignment = HAlignment.Left,
        };
        _release.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Release, notify: _notify.Pressed));
        Body.AddChild(_release);
    }

    /// <summary>How many owners the tab lists, for tests.</summary>
    public int OwnerCount { get; private set; }

    protected override void Show(WFShipAdminDetail detail, AdminFlags flags)
    {
        _flags = flags;
        OwnerCount = detail.Owners.Count;
        _owners.RemoveAllChildren();
        _owners.AddChild(Line(Loc.GetString("wf-ship-admin-overview-record-owner"),
            detail.RecordOwner.Length > 0 ? detail.RecordOwner : Loc.GetString("wf-ship-admin-none")));
        if (detail.Owners.Count == 0)
            _owners.AddChild(Note(Loc.GetString("wf-ship-admin-overview-no-owners")));

        foreach (var owner in detail.Owners)
            _owners.AddChild(OwnerRow(owner, flags, detail.Owners.Count > 1));

        var hangar = (detail.Flags & WFShipAdminRowFlags.Hangar) != 0;
        _detachHangar.Visible = hangar;
        Gate(_detachHangar, WFShipAdminAction.DetachHangar, flags);
        _leaveCrewMode.Visible = (detail.Flags & WFShipAdminRowFlags.CrewLinked) != 0;
        _dropCompany.Visible = (detail.Flags & WFShipAdminRowFlags.Company) != 0;

        Gate(_release, WFShipAdminAction.Release, flags);
        UpdateButtons();
    }

    private Control OwnerRow(WFShipAdminOwner owner, AdminFlags flags, bool removable)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(Line(WFShipAdminOverviewTab.OwnerKind(owner.Kind), WFShipAdminOverviewTab.OwnerText(owner), false));

        var remove = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-ownership-remove"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
            ToolTip = removable ? null : Loc.GetString("wf-ship-admin-ownership-remove-last"),
        };
        Gate(remove, WFShipAdminAction.RemoveOwner, flags, removable);
        remove.OnPressed += _ => Send(new WFShipAdminOwnerMessage(Grid, false, owner.User, owner.Card, _notify.Pressed));
        row.AddChild(remove);
        return row;
    }

    private void OnPlayerPicked(PlayerInfo? player)
    {
        _player = player;
        _picked.Text = player == null
            ? Loc.GetString("wf-ship-admin-ownership-nobody-picked")
            : Loc.GetString("wf-ship-admin-ownership-picked", ("name", player.CharacterName), ("username", player.Username));
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        Gate(_transfer, WFShipAdminAction.Transfer, _flags, _player != null);
        Gate(_addOwner, WFShipAdminAction.AddOwner, _flags, _player != null);
    }

    private void SendTransfer()
    {
        if (_player == null)
            return;

        Send(new WFShipAdminTransferMessage(Grid, _player.SessionId)
        {
            ClearAllowList = _clearAllowList.Pressed,
            KeepPrevious = _keepPrevious.Pressed,
            DropCompany = _dropCompany.Pressed,
            LeaveCrewMode = _leaveCrewMode.Pressed,
            DetachHangar = _detachHangar.Visible && _detachHangar.Pressed,
            Faction = (WFShipAdminFaction) _faction.SelectedId,
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
