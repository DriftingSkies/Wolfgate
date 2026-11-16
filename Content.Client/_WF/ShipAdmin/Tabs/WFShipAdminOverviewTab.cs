using System.Globalization;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ShipAdmin.Tabs;

/// <summary>What a ship is, who owns it, who is aboard and how its access stands. Changes nothing.</summary>
public sealed class WFShipAdminOverviewTab : WFShipAdminTab
{
    private readonly BoxContainer _lines;
    private readonly Button _scan;

    public override string Title => Loc.GetString("wf-ship-admin-tab-overview");

    public WFShipAdminOverviewTab()
    {
        _lines = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        _scan = new Button
        {
            Text = Loc.GetString("wf-ship-admin-scan"),
            ToolTip = Loc.GetString("wf-ship-admin-scan-tooltip"),
            StyleClasses = { "ButtonSquare" },
            HorizontalAlignment = HAlignment.Left,
        };
        _scan.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.Scan));

        Body.AddChild(_lines);
        Body.AddChild(_scan);
    }

    /// <summary>How many lines the tab shows, for tests.</summary>
    public int LineCount => _lines.ChildCount;

    protected override void Show(WFShipAdminDetail detail, AdminFlags flags)
    {
        Gate(_scan, WFShipAdminAction.Scan, flags);
        _lines.RemoveAllChildren();

        _lines.AddChild(Heading(Loc.GetString("wf-ship-admin-overview-ship")));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-design"), Or(detail.Design)));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-entity"), detail.Grid.ToString()));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-place"), Loc.GetString("wf-ship-admin-overview-place-value",
            ("map", Or(detail.Map)),
            ("x", detail.Position.X.ToString("0", CultureInfo.InvariantCulture)),
            ("y", detail.Position.Y.ToString("0", CultureInfo.InvariantCulture)))));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-mass"), detail.Mass.ToString("0", CultureInfo.InvariantCulture)));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-docked"), detail.DockedTo.Count > 0 ? string.Join(", ", detail.DockedTo) : Or(string.Empty)));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-value"),
            detail.Value is { } value ? Loc.GetString("wf-ship-admin-overview-value-amount", ("value", value)) : Loc.GetString("wf-ship-admin-overview-not-scanned")));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-state"), Tags(detail.Flags)));

        _lines.AddChild(Heading(Loc.GetString("wf-ship-admin-overview-owners")));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-record-owner"), Or(detail.RecordOwner)));
        _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-registered"), detail.Registered.Length > 0
            ? Loc.GetString("wf-ship-admin-overview-account", ("name", detail.Registered), ("online", detail.RegisteredOnline ? "true" : "false"))
            : Or(string.Empty)));
        if (detail.Owners.Count == 0)
            _lines.AddChild(Note(Loc.GetString("wf-ship-admin-overview-no-owners")));

        foreach (var owner in detail.Owners)
            _lines.AddChild(Line(OwnerKind(owner.Kind), OwnerText(owner)));

        _lines.AddChild(Heading(Loc.GetString("wf-ship-admin-overview-access")));
        if (!detail.HasAccess)
        {
            _lines.AddChild(Note(Loc.GetString("wf-ship-admin-overview-no-access")));
        }
        else
        {
            _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-mode"), Mode(detail.Mode)));
            _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-lock"), Loc.GetString(detail.Locked ? "wf-ship-admin-locked" : "wf-ship-admin-unlocked")));
            _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-allowed"), detail.AllowList.Count.ToString()));
            if (detail.Crew.Length > 0)
                _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-crew"), detail.Crew));
        }

        if (detail.Company.Length > 0)
            _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-company"), detail.Company));

        if (detail.Faction.Length > 0)
            _lines.AddChild(Line(Loc.GetString("wf-ship-admin-overview-faction"), detail.Faction));

        _lines.AddChild(Heading(Loc.GetString("wf-ship-admin-overview-aboard", ("count", detail.OccupantCount))));
        foreach (var occupant in detail.Occupants)
            _lines.AddChild(Line(occupant.Name, occupant.Username));

        if (detail.OccupantCount > detail.Occupants.Count)
            _lines.AddChild(Note(Loc.GetString("wf-ship-admin-overview-aboard-more", ("count", detail.OccupantCount - detail.Occupants.Count))));
    }

    /// <summary>A mode's name, as every tab shows it.</summary>
    public static string Mode(Content.Shared._WF.ShipAccess.WFShipAccessMode mode)
    {
        return Loc.GetString($"wf-ship-admin-mode-{mode.ToString().ToLowerInvariant()}");
    }

    /// <summary>How someone owns the ship, as every tab shows it.</summary>
    public static string OwnerKind(WFShipAdminOwnerKind kind)
    {
        return Loc.GetString($"wf-ship-admin-owner-{kind.ToString().ToLowerInvariant()}");
    }

    /// <summary>An owner's name with their account and whether they are connected.</summary>
    public static string OwnerText(WFShipAdminOwner owner)
    {
        if (owner.User == null)
            return Loc.GetString("wf-ship-admin-owner-unstamped", ("name", owner.Name));

        return owner.Kind == WFShipAdminOwnerKind.Account
            ? Loc.GetString("wf-ship-admin-overview-account", ("name", owner.Username), ("online", owner.Online ? "true" : "false"))
            : Loc.GetString("wf-ship-admin-owner-stamped", ("name", owner.Name), ("username", owner.Username), ("online", owner.Online ? "true" : "false"));
    }

    private static string Or(string text)
    {
        return text.Length > 0 ? text : Loc.GetString("wf-ship-admin-none");
    }

    private static string Tags(WFShipAdminRowFlags flags)
    {
        var tags = new List<string>();
        foreach (var flag in Enum.GetValues<WFShipAdminRowFlags>())
        {
            if (flag != WFShipAdminRowFlags.None && (flags & flag) != 0)
                tags.Add(Loc.GetString($"wf-ship-admin-tag-{flag.ToString().ToLowerInvariant()}"));
        }

        return tags.Count > 0 ? string.Join(", ", tags) : Loc.GetString("wf-ship-admin-none");
    }
}
