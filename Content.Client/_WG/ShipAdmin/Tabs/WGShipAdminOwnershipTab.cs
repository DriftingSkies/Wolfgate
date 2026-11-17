using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>Who owns the ship, and handing it to someone else: transfer, add a co-owner, remove an owner, release.</summary>
public sealed class WGShipAdminOwnershipTab : WGShipAdminTab
{
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

    private PlayerInfo? _player;
    private AdminFlags _flags;

    public override string Title => Loc.GetString("wg-ship-admin-tab-ownership");

    public WGShipAdminOwnershipTab()
    {
        _owners = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-overview-owners")));
        Body.AddChild(_owners);

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
        UpdateButtons();
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
