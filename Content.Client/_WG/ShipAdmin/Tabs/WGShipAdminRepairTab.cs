using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>Putting a ship right: a fix that keeps what players built, and the snapshot it rebuilds from.</summary>
public sealed class WGShipAdminRepairTab : WGShipAdminTab
{
    private readonly WGShipAdminText _damage;
    private readonly WGShipAdminText _progress;
    private readonly Dictionary<WGShipAdminFixParts, CheckBox> _parts = new();
    private readonly Button _scan;
    private readonly Button _fix;
    private readonly WGShipAdminText _snapshot;
    private readonly ConfirmButton _retake;
    private readonly ConfirmButton _restore;
    private readonly List<Button> _quick = new();

    /// <summary>The parts with a one-press button of their own.</summary>
    private static readonly WGShipAdminFixParts[] QuickParts =
    {
        WGShipAdminFixParts.Air,
        WGShipAdminFixParts.Fires,
        WGShipAdminFixParts.Trash,
        WGShipAdminFixParts.Puddles,
        WGShipAdminFixParts.Lights,
    };

    public override string Title => Loc.GetString("wg-ship-admin-tab-repair");

    public WGShipAdminRepairTab()
    {
        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-repair-fix-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-repair-fix-note")));

        _damage = Status();
        Body.AddChild(_damage);
        _progress = Note(string.Empty);
        Body.AddChild(_progress);

        var boxes = new GridContainer { Columns = 2 };
        foreach (var part in Enum.GetValues<WGShipAdminFixParts>())
        {
            if (part is WGShipAdminFixParts.None or WGShipAdminFixParts.All or WGShipAdminFixParts.Default)
                continue;

            var key = $"wg-ship-admin-repair-part-{part.ToString().ToLowerInvariant()}";
            var box = new CheckBox
            {
                Text = Loc.GetString(key),
                ToolTip = Loc.GetString($"{key}-tooltip"),
                Pressed = (WGShipAdminFixParts.Default & part) != 0,
            };
            _parts[part] = box;
            boxes.AddChild(box);
        }

        Body.AddChild(boxes);

        _scan = new Button { Text = Loc.GetString("wg-ship-admin-repair-scan"), StyleClasses = { "ButtonSquare" } };
        _scan.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Scan));
        _fix = new Button { Text = Loc.GetString("wg-ship-admin-repair-fix"), StyleClasses = { "ButtonSquare" } };
        _fix.OnPressed += _ => Send(new WGShipAdminFixMessage(Grid, Parts()));

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        buttons.AddChild(_scan);
        buttons.AddChild(_fix);
        Body.AddChild(buttons);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-repair-quick-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-repair-quick-note")));
        var quick = new GridContainer { Columns = 3 };
        foreach (var part in QuickParts)
        {
            var key = $"wg-ship-admin-repair-quick-{part.ToString().ToLowerInvariant()}";
            var button = new Button { Text = Loc.GetString(key), ToolTip = Loc.GetString($"wg-ship-admin-repair-part-{part.ToString().ToLowerInvariant()}-tooltip"), StyleClasses = { "ButtonSquare" } };
            var only = part;
            button.OnPressed += _ => Send(new WGShipAdminFixMessage(Grid, only));
            _quick.Add(button);
            quick.AddChild(button);
        }

        Body.AddChild(quick);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-repair-snapshot-heading")));
        _snapshot = Note(string.Empty);
        Body.AddChild(_snapshot);
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-repair-snapshot-note")));

        _retake = new ConfirmButton
        {
            Text = Loc.GetString("wg-ship-admin-repair-retake"),
            ToolTip = Loc.GetString("wg-ship-admin-repair-retake-tooltip"),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _retake.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Snapshot));
        _restore = new ConfirmButton
        {
            Text = Loc.GetString("wg-ship-admin-repair-restore"),
            ToolTip = Loc.GetString("wg-ship-admin-repair-restore-tooltip"),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _restore.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Restore));

        var snapshotButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        snapshotButtons.AddChild(_retake);
        snapshotButtons.AddChild(_restore);
        Body.AddChild(snapshotButtons);
    }

    /// <summary>The parts ticked for a fix, for tests.</summary>
    public WGShipAdminFixParts Parts()
    {
        var parts = WGShipAdminFixParts.None;
        foreach (var (part, box) in _parts)
        {
            if (box.Pressed)
                parts |= part;
        }

        return parts;
    }

    protected override void Show(WGShipAdminDetail detail, AdminFlags flags)
    {
        var hasSnapshot = (detail.Flags & WGShipAdminRowFlags.Snapshot) != 0;
        var fixing = detail.FixTotal > 0 || (detail.Flags & WGShipAdminRowFlags.Fixing) != 0;

        _damage.Text = detail.Damage is { } damage
            ? Loc.GetString("wg-ship-admin-repair-damage", ("tiles", damage.Tiles), ("parts", damage.Parts), ("damaged", damage.Damaged))
            : Loc.GetString("wg-ship-admin-repair-not-scanned");
        _progress.Visible = fixing;
        _progress.Text = Loc.GetString("wg-ship-admin-repair-progress", ("done", detail.FixDone), ("total", detail.FixTotal));
        _snapshot.Text = Loc.GetString(hasSnapshot ? "wg-ship-admin-repair-has-snapshot" : "wg-ship-admin-repair-no-snapshot");

        Gate(_scan, WGShipAdminAction.Scan, flags);
        Gate(_fix, WGShipAdminAction.Fix, flags, !fixing);
        foreach (var button in _quick)
            Gate(button, WGShipAdminAction.Fix, flags, !fixing);
        Gate(_retake, WGShipAdminAction.Snapshot, flags, !fixing);
        Gate(_restore, WGShipAdminAction.Restore, flags, hasSnapshot && !fixing);
    }
}
