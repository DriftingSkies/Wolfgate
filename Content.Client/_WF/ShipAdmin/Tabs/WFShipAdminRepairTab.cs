using Content.Client.UserInterface.Controls;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ShipAdmin.Tabs;

/// <summary>Putting a ship right: a fix that keeps what players built, and the snapshot it rebuilds from.</summary>
public sealed class WFShipAdminRepairTab : WFShipAdminTab
{
    private readonly WFShipAdminText _damage;
    private readonly WFShipAdminText _progress;
    private readonly Dictionary<WFShipAdminFixParts, CheckBox> _parts = new();
    private readonly Button _scan;
    private readonly Button _fix;
    private readonly WFShipAdminText _snapshot;
    private readonly ConfirmButton _retake;
    private readonly ConfirmButton _restore;

    public override string Title => Loc.GetString("wf-ship-admin-tab-repair");

    public WFShipAdminRepairTab()
    {
        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-repair-fix-heading")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-repair-fix-note")));

        _damage = Status();
        Body.AddChild(_damage);
        _progress = Note(string.Empty);
        Body.AddChild(_progress);

        var boxes = new GridContainer { Columns = 2 };
        foreach (var part in Enum.GetValues<WFShipAdminFixParts>())
        {
            if (part is WFShipAdminFixParts.None or WFShipAdminFixParts.All)
                continue;

            var key = $"wf-ship-admin-repair-part-{part.ToString().ToLowerInvariant()}";
            var box = new CheckBox { Text = Loc.GetString(key), ToolTip = Loc.GetString($"{key}-tooltip"), Pressed = true };
            _parts[part] = box;
            boxes.AddChild(box);
        }

        Body.AddChild(boxes);

        _scan = new Button { Text = Loc.GetString("wf-ship-admin-repair-scan"), StyleClasses = { "ButtonSquare" } };
        _scan.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Scan));
        _fix = new Button { Text = Loc.GetString("wf-ship-admin-repair-fix"), StyleClasses = { "ButtonSquare" } };
        _fix.OnPressed += _ => Send(new WFShipAdminFixMessage(Grid, Parts()));

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        buttons.AddChild(_scan);
        buttons.AddChild(_fix);
        Body.AddChild(buttons);

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-repair-snapshot-heading")));
        _snapshot = Note(string.Empty);
        Body.AddChild(_snapshot);
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-repair-snapshot-note")));

        _retake = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-repair-retake"),
            ToolTip = Loc.GetString("wf-ship-admin-repair-retake-tooltip"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _retake.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Snapshot));
        _restore = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-repair-restore"),
            ToolTip = Loc.GetString("wf-ship-admin-repair-restore-tooltip"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _restore.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Restore));

        var snapshotButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        snapshotButtons.AddChild(_retake);
        snapshotButtons.AddChild(_restore);
        Body.AddChild(snapshotButtons);
    }

    /// <summary>The parts ticked for a fix, for tests.</summary>
    public WFShipAdminFixParts Parts()
    {
        var parts = WFShipAdminFixParts.None;
        foreach (var (part, box) in _parts)
        {
            if (box.Pressed)
                parts |= part;
        }

        return parts;
    }

    protected override void Show(WFShipAdminDetail detail, AdminFlags flags)
    {
        var hasSnapshot = (detail.Flags & WFShipAdminRowFlags.Snapshot) != 0;
        var fixing = detail.FixTotal > 0 || (detail.Flags & WFShipAdminRowFlags.Fixing) != 0;

        _damage.Text = detail.Damage is { } damage
            ? Loc.GetString("wf-ship-admin-repair-damage", ("tiles", damage.Tiles), ("parts", damage.Parts), ("damaged", damage.Damaged))
            : Loc.GetString("wf-ship-admin-repair-not-scanned");
        _progress.Visible = fixing;
        _progress.Text = Loc.GetString("wf-ship-admin-repair-progress", ("done", detail.FixDone), ("total", detail.FixTotal));
        _snapshot.Text = Loc.GetString(hasSnapshot ? "wf-ship-admin-repair-has-snapshot" : "wf-ship-admin-repair-no-snapshot");

        Gate(_scan, WFShipAdminAction.Scan, flags);
        Gate(_fix, WFShipAdminAction.Fix, flags, !fixing);
        Gate(_retake, WFShipAdminAction.Snapshot, flags, !fixing);
        Gate(_restore, WFShipAdminAction.Restore, flags, hasSnapshot && !fixing);
    }
}
