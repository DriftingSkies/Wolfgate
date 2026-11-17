using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Robust.Server.Player;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WG.ShipAdmin;

/// <summary>
/// The admin ship panel: lists ships and applies what staff do to one from it. Each open panel is a
/// <see cref="WGShipAdminEui"/>; this system builds their state, keeps them fresh and runs their actions.
/// </summary>
public sealed partial class WGShipAdminSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly TimeSpan DetailInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ListInterval = TimeSpan.FromSeconds(10);
    private static readonly SpriteSpecifier VerbIcon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/settings.svg.192dpi.png"));

    private readonly Dictionary<ICommonSession, WGShipAdminEui> _panels = new();
    private TimeSpan _detailTimer;
    private TimeSpan _listTimer;

    /// <summary>The panels that are open, one per admin.</summary>
    public IReadOnlyCollection<WGShipAdminEui> Panels => _panels.Values;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<WGShipAdminOpenActionEvent>(OnOpenAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateFixes();
        UpdateActions(frameTime);
        if (_panels.Count == 0)
            return;

        var elapsed = TimeSpan.FromSeconds(frameTime);
        _detailTimer += elapsed;
        _listTimer += elapsed;

        var list = _listTimer >= ListInterval;
        if (list)
            _listTimer = TimeSpan.Zero;

        if (!list && _detailTimer < DetailInterval)
            return;

        _detailTimer = TimeSpan.Zero;
        foreach (var panel in _panels.Values)
        {
            if (list)
                panel.RefreshList();
            else
                panel.RefreshDetail();
        }
    }

    /// <summary>
    /// Opens the panel for an admin, or reselects in the one they have open. A new panel with no ship named starts
    /// on the ship the admin is over.
    /// </summary>
    public void Open(ICommonSession admin, EntityUid? grid)
    {
        if (_panels.TryGetValue(admin, out var open))
        {
            if (grid != null)
                open.Select(grid);

            open.RefreshList();
            return;
        }

        _eui.OpenEui(new WGShipAdminEui(grid ?? ShipUnder(admin)), admin);
    }

    public void PanelOpened(WGShipAdminEui panel)
    {
        _panels[panel.Player] = panel;
    }

    public void PanelClosed(WGShipAdminEui panel)
    {
        if (_panels.TryGetValue(panel.Player, out var open) && open == panel)
            _panels.Remove(panel.Player);
    }

    /// <summary>The flags an admin holds right now; none while de-adminned.</summary>
    public AdminFlags Flags(ICommonSession admin)
    {
        return _admin.GetAdminData(admin)?.Flags ?? AdminFlags.None;
    }

    /// <summary>Whether the admin may run an action, by <see cref="WGShipAdminPermissions"/>.</summary>
    public bool Allowed(ICommonSession admin, WGShipAdminAction action)
    {
        return WGShipAdminPermissions.Allowed(Flags(admin), action);
    }

    /// <summary>Runs an action that needs no more than a switch; the result line goes back to the panel.</summary>
    public void Run(WGShipAdminEui panel, EntityUid grid, WGShipAdminAction action, bool value, bool notify)
    {
        switch (action)
        {
            case WGShipAdminAction.Scan:
                panel.Scanned(grid, Appraise(grid), Survey(grid));
                panel.SetResult(Loc.GetString("wg-ship-admin-result-scanned", ("ship", Name(grid))));
                break;
            case WGShipAdminAction.Snapshot:
                panel.Finish(Snapshot(panel.Player, grid, out var snapped), snapped);
                break;
            case WGShipAdminAction.Restore:
                panel.Finish(Restore(panel.Player, grid, out var restored), restored);
                break;
            case WGShipAdminAction.Undock:
                panel.Finish(Undock(panel.Player, grid, out var undocked), undocked);
                break;
            case WGShipAdminAction.Halt:
                panel.Finish(Halt(panel.Player, grid, out var halted), halted);
                break;
            case WGShipAdminAction.ClearPilots:
                panel.Finish(ClearPilots(panel.Player, grid, out var unpiloted), unpiloted);
                break;
            case WGShipAdminAction.FtlLock:
                panel.Finish(SetFtlLock(panel.Player, grid, value, out var ftlLocked), ftlLocked);
                break;
            case WGShipAdminAction.ResetFtl:
                panel.Finish(ResetFtl(panel.Player, grid, out var ftlReset), ftlReset);
                break;
            case WGShipAdminAction.Shields:
                panel.Finish(SetShields(panel.Player, grid, value, out var shielded), shielded);
                break;
            case WGShipAdminAction.Power:
                panel.Finish(SetPower(panel.Player, grid, value, out var powered), powered);
                break;
            case WGShipAdminAction.Fitting:
                panel.Finish(FitBestParts(panel.Player, grid, out var fitted), fitted);
                break;
            case WGShipAdminAction.Eject:
                panel.Finish(Eject(panel.Player, grid, out var ejected), ejected);
                break;
            case WGShipAdminAction.Strip:
                panel.Finish(Strip(panel.Player, grid, out var stripped), stripped);
                break;
            case WGShipAdminAction.Delete:
                panel.Finish(Delete(panel.Player, grid, value, out var deleted), deleted);
                break;
            case WGShipAdminAction.Release:
                panel.Finish(Release(panel.Player, grid, notify, out var released), released);
                break;
            case WGShipAdminAction.SetLocked:
                panel.Finish(SetLocked(panel.Player, grid, value, notify, out var locked), locked);
                break;
            case WGShipAdminAction.AllowClear:
                panel.Finish(AllowClear(panel.Player, grid, notify, out var cleared), cleared);
                break;
            case WGShipAdminAction.ResetDoors:
                panel.Finish(ResetDoors(panel.Player, grid, notify, out var reset), reset);
                break;
            default:
                panel.SetResult(Loc.GetString("wg-ship-admin-result-unknown"), true);
                break;
        }
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor) || !_admin.HasAdminFlag(actor.PlayerSession, WGShipAdminPermissions.Base))
            return;

        var grid = HasComp<MapGridComponent>(args.Target) ? args.Target : Transform(args.Target).GridUid;
        if (grid is not { } ship)
            return;

        var player = actor.PlayerSession;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wg-ship-admin-verb"),
            Message = Loc.GetString("wg-ship-admin-verb-description", ("ship", Name(ship))),
            Category = VerbCategory.Admin,
            Icon = VerbIcon,
            Act = () => Open(player, ship),
            Impact = LogImpact.Low,
        });
    }

    /// <summary>Writes one panel action to the admin log.</summary>
    private void Audit(ICommonSession admin, LogImpact impact, EntityUid grid, string what)
    {
        _adminLog.Add(LogType.WGShipAdmin, impact, $"{admin.Name:actor} used Shipmaster on {ToPrettyString(grid):grid}: {what}");
    }
}
