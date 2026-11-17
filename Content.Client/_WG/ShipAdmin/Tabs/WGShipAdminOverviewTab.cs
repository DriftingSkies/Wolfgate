using System.Globalization;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>What a ship is, who owns it, who is aboard and how its access stands. Changes nothing.</summary>
public sealed class WGShipAdminOverviewTab : WGShipAdminTab
{
    private readonly BoxContainer _lines;
    private readonly Button _scan;

    public override string Title => Loc.GetString("wg-ship-admin-tab-overview");

    public WGShipAdminOverviewTab()
    {
        _lines = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        _scan = new Button
        {
            Text = Loc.GetString("wg-ship-admin-scan"),
            ToolTip = Loc.GetString("wg-ship-admin-scan-tooltip"),
            StyleClasses = { "ButtonSquare" },
            HorizontalAlignment = HAlignment.Left,
        };
        _scan.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Scan));

        Body.AddChild(_lines);
        Body.AddChild(_scan);
    }

    /// <summary>How many lines the tab shows, for tests.</summary>
    public int LineCount => _lines.ChildCount;

    protected override void Show(WGShipAdminDetail detail, AdminFlags flags)
    {
        Gate(_scan, WGShipAdminAction.Scan, flags);
        _lines.RemoveAllChildren();

        _lines.AddChild(Heading(Loc.GetString("wg-ship-admin-overview-ship")));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-design"), Or(detail.Design)));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-entity"), detail.Grid.ToString()));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-place"), Loc.GetString("wg-ship-admin-overview-place-value",
            ("map", Or(detail.Map)),
            ("x", detail.Position.X.ToString("0", CultureInfo.InvariantCulture)),
            ("y", detail.Position.Y.ToString("0", CultureInfo.InvariantCulture)))));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-mass"), detail.Mass.ToString("0", CultureInfo.InvariantCulture)));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-docked"), detail.DockedTo.Count > 0 ? string.Join(", ", detail.DockedTo) : Or(string.Empty)));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-value"),
            detail.Value is { } value ? Loc.GetString("wg-ship-admin-overview-value-amount", ("value", value)) : Loc.GetString("wg-ship-admin-overview-not-scanned")));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-state"), Tags(detail.Flags)));

        _lines.AddChild(Heading(Loc.GetString("wg-ship-admin-overview-owners")));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-record-owner"), Or(detail.RecordOwner)));
        _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-registered"), detail.Registered.Length > 0
            ? Loc.GetString("wg-ship-admin-overview-account", ("name", detail.Registered), ("online", detail.RegisteredOnline ? "true" : "false"))
            : Or(string.Empty)));
        if (detail.Owners.Count == 0)
            _lines.AddChild(Note(Loc.GetString("wg-ship-admin-overview-no-owners")));

        foreach (var owner in detail.Owners)
            _lines.AddChild(Line(OwnerKind(owner.Kind), OwnerText(owner)));

        _lines.AddChild(Heading(Loc.GetString("wg-ship-admin-overview-access")));
        if (!detail.HasAccess)
        {
            _lines.AddChild(Note(Loc.GetString("wg-ship-admin-overview-no-access")));
        }
        else
        {
            _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-mode"), Mode(detail.Mode)));
            _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-lock"), Loc.GetString(detail.Locked ? "wg-ship-admin-locked" : "wg-ship-admin-unlocked")));
            _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-allowed"), detail.AllowList.Count.ToString()));
            if (detail.Crew.Length > 0)
                _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-crew"), detail.Crew));
        }

        if (detail.Company.Length > 0)
            _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-company"), detail.Company));

        if (detail.Faction.Length > 0)
            _lines.AddChild(Line(Loc.GetString("wg-ship-admin-overview-faction"), detail.Faction));

        _lines.AddChild(Heading(Loc.GetString("wg-ship-admin-overview-aboard", ("count", detail.OccupantCount))));
        foreach (var occupant in detail.Occupants)
            _lines.AddChild(Line(occupant.Name, occupant.Username));

        if (detail.OccupantCount > detail.Occupants.Count)
            _lines.AddChild(Note(Loc.GetString("wg-ship-admin-overview-aboard-more", ("count", detail.OccupantCount - detail.Occupants.Count))));
    }

    /// <summary>A mode's name, as every tab shows it.</summary>
    public static string Mode(Content.Shared._WG.ShipAccess.WGShipAccessMode mode)
    {
        return Loc.GetString($"wg-ship-admin-mode-{mode.ToString().ToLowerInvariant()}");
    }

    /// <summary>How someone owns the ship, as every tab shows it.</summary>
    public static string OwnerKind(WGShipAdminOwnerKind kind)
    {
        return Loc.GetString($"wg-ship-admin-owner-{kind.ToString().ToLowerInvariant()}");
    }

    /// <summary>An owner's name with their account and whether they are connected.</summary>
    public static string OwnerText(WGShipAdminOwner owner)
    {
        if (owner.User == null)
            return Loc.GetString("wg-ship-admin-owner-unstamped", ("name", owner.Name));

        return owner.Kind == WGShipAdminOwnerKind.Account
            ? Loc.GetString("wg-ship-admin-overview-account", ("name", owner.Username), ("online", owner.Online ? "true" : "false"))
            : Loc.GetString("wg-ship-admin-owner-stamped", ("name", owner.Name), ("username", owner.Username), ("online", owner.Online ? "true" : "false"));
    }

    private static string Or(string text)
    {
        return text.Length > 0 ? text : Loc.GetString("wg-ship-admin-none");
    }

    private static string Tags(WGShipAdminRowFlags flags)
    {
        var tags = new List<string>();
        foreach (var flag in Enum.GetValues<WGShipAdminRowFlags>())
        {
            if (flag != WGShipAdminRowFlags.None && (flags & flag) != 0)
                tags.Add(Loc.GetString($"wg-ship-admin-tag-{flag.ToString().ToLowerInvariant()}"));
        }

        return tags.Count > 0 ? string.Join(", ", tags) : Loc.GetString("wg-ship-admin-none");
    }
}
