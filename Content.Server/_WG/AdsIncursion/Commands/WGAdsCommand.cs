using System.Globalization;
using System.Linq;
using Content.Server._WG.AdsIncursion.Components;
using Content.Server._WG.AdsIncursion.Systems;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.SectorControl.Systems;
using Content.Server.Administration;
using Content.Shared._WG.AdsIncursion;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.SectorControl;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.AdsIncursion.Commands;

/// <summary>The ADS incursion from the console: show it, found or deploy a node, send a sentinel, sweep, interdiction, challenge or the dreadnought, set the tier, step it, pause it or clear it.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGAdsCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGAdsCampaignSystem _campaign = default!;
    [Dependency] private WGSectorTerritorySystem _territory = default!;

    private static readonly string[] Subcommands =
        { "status", "found", "deploy", "sentinel", "tier", "spread", "sweep", "interdict", "challenge", "dreadnought", "pause", "resume", "clear" };

    public override string Command => "wg_ads";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "status" when args.Length == 1:
                Status(shell);
                break;
            case "found" when args.Length is 1 or 3:
                Found(shell, args);
                break;
            case "deploy" when args.Length == 1:
                Deploy(shell);
                break;
            case "sentinel" when args.Length == 2:
                Sentinel(shell, args[1]);
                break;
            case "tier" when args.Length == 2:
                Tier(shell, args[1]);
                break;
            case "spread" when args.Length == 1:
                if (_campaign.TrySpreadOnce())
                    shell.WriteLine(Loc.GetString("cmd-wg_ads-spread"));
                else
                    Failed(shell, "cmd-wg_ads-spread-failed");
                break;
            case "sweep" when args.Length == 1:
                Sweep(shell);
                break;
            case "interdict" when args.Length == 2:
                Interdict(shell, args[1]);
                break;
            case "challenge" when args.Length is 1 or 3:
                Challenge(shell, args);
                break;
            case "dreadnought" when args.Length == 1:
                Dreadnought(shell);
                break;
            case "pause" when args.Length == 1:
                _campaign.Paused = true;
                shell.WriteLine(Loc.GetString("cmd-wg_ads-paused"));
                break;
            case "resume" when args.Length == 1:
                _campaign.Paused = false;
                shell.WriteLine(Loc.GetString("cmd-wg_ads-resumed"));
                break;
            case "clear" when args.Length == 1:
                shell.WriteLine(Loc.GetString("cmd-wg_ads-cleared", ("count", _campaign.ClearCampaign())));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wg_ads-hint-sub")),
            2 when args[0] == "found" => CompletionResult.FromHint(Loc.GetString("cmd-wg_ads-hint-x")),
            3 when args[0] == "found" => CompletionResult.FromHint(Loc.GetString("cmd-wg_ads-hint-y")),
            2 when args[0] == "challenge" => CompletionResult.FromHint(Loc.GetString("cmd-wg_ads-hint-q")),
            3 when args[0] == "challenge" => CompletionResult.FromHint(Loc.GetString("cmd-wg_ads-hint-r")),
            2 when args[0] == "interdict" => CompletionResult.FromHint(Loc.GetString("cmd-wg_ads-hint-grid")),
            2 when args[0] == "sentinel" => CompletionResult.FromHint(Loc.GetString("cmd-wg_ads-hint-node")),
            2 when args[0] == "tier" => CompletionResult.FromHintOptions(
                Enumerable.Range(0, WGAdsCampaignSystem.MaxTier + 1).Select(tier => tier.ToString()),
                Loc.GetString("cmd-wg_ads-hint-tier")),
            _ => CompletionResult.Empty,
        };
    }

    private void Status(IConsoleShell shell)
    {
        if (_campaign.Campaign is not { } campaign)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-no-campaign"));
            return;
        }

        var state = _campaign.State();
        var nodes = _campaign.Nodes();
        var sweeps = _campaign.Sweeps();
        var now = _timing.CurTime;
        shell.WriteLine(Loc.GetString("cmd-wg_ads-status-header",
            ("campaign", campaign.ID),
            ("running", _campaign.Running.ToString()),
            ("paused", _campaign.Held.ToString()),
            ("tier", _campaign.TierName(state?.Tier ?? 0)),
            ("score", (state?.Score ?? 0f).ToString("0.0", CultureInfo.InvariantCulture))));
        shell.WriteLine(Loc.GetString("cmd-wg_ads-status-counts",
            ("nodes", nodes.Count),
            ("cells", _territory.Held(_territory.SectorMap, campaign.Faction).Count),
            ("sweeps", sweeps.Count),
            ("challenges", _campaign.Interdictors().Count),
            ("pickets", _campaign.Pickets().Count)));
        shell.WriteLine(Loc.GetString("cmd-wg_ads-status-clocks",
            ("spread", Seconds(state?.NextSpread, now)),
            ("challenge", Seconds(state?.NextChallenge, now))));

        if (state is { Finished: true })
            shell.WriteLine(Loc.GetString("cmd-wg_ads-status-finished"));
        else if (campaign.Dreadnought != null && state?.OverrunSince is { } overrun && _campaign.Dreadnoughts().Count == 0)
            shell.WriteLine(Loc.GetString("cmd-wg_ads-status-dreadnought-due", ("seconds", Seconds(overrun + campaign.DreadnoughtDelay, now))));

        if (nodes.Count == 0 && state is not { Finished: true })
            shell.WriteLine(WhyNoNode(campaign, state, now));

        foreach (var (uid, node, _) in nodes)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-status-node",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WGSectorHex.Callsign(node.Cell)),
                ("hits", node.Hits),
                ("sweeps", sweeps.Count(sweep => sweep.Comp1.Node == uid)),
                ("sentinels", node.Sentinels.Count),
                ("inbound", node.Inbound != null ? Loc.GetString("cmd-wg_ads-status-node-inbound") : string.Empty)));
        }

        var transform = EntityManager.System<SharedTransformSystem>();
        foreach (var (uid, dreadnought, encounter) in _campaign.Dreadnoughts())
        {
            if (!encounter.Ships.TryGetValue(dreadnought.Lead, out var ship) || !EntityManager.EntityExists(ship.Grid))
                continue;

            var at = transform.GetWorldPosition(ship.Grid);
            var target = dreadnought.Target ?? dreadnought.Station;
            var range = target is { } aim && EntityManager.EntityExists(aim) ? WGReaverCampaignSystem.Kilometres(at, Lead(aim, transform) ?? at) : "-";
            shell.WriteLine(Loc.GetString(dreadnought.Target != null ? "cmd-wg_ads-status-dreadnought-hunting" : "cmd-wg_ads-status-dreadnought-holding",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("target", target is { } named && EntityManager.TryGetComponent<MetaDataComponent>(named, out var meta) ? meta.EntityName : "-"),
                ("range", range),
                ("seconds", Math.Max(0, (int) (now - dreadnought.Launched).TotalSeconds))));
        }

        foreach (var (uid, deployment, encounter) in _campaign.Deployments())
        {
            var at = encounter.Ships.TryGetValue(deployment.Lead, out var lead) && EntityManager.EntityExists(lead.Grid)
                ? transform.GetWorldPosition(lead.Grid)
                : encounter.Origin.Position;
            shell.WriteLine(Loc.GetString("cmd-wg_ads-status-deployment",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WGSectorHex.Callsign(deployment.Target)),
                ("range", WGReaverCampaignSystem.Kilometres(at, deployment.TargetPosition)),
                ("seconds", Math.Max(0, (int) (now - deployment.Launched).TotalSeconds))));
        }

        foreach (var (uid, interdictor, _) in _campaign.Interdictors())
        {
            var contest = _territory.ContestOf(_territory.SectorMap, interdictor.Target);
            shell.WriteLine(Loc.GetString("cmd-wg_ads-status-challenge",
                ("uid", EntityManager.GetNetEntity(uid).ToString()),
                ("callsign", WGSectorHex.Callsign(interdictor.Target)),
                ("arrived", interdictor.Arrived.ToString()),
                ("seconds", Seconds(contest?.Deadline, now))));
        }

        foreach (var (uid, sweep, _) in sweeps)
        {
            if (sweep.Interdicting is not { } target)
                continue;

            shell.WriteLine(Loc.GetString("cmd-wg_ads-status-interdict",
                ("sweep", EntityManager.GetNetEntity(uid).ToString()),
                ("ship", EntityManager.TryGetComponent<MetaDataComponent>(target, out var meta) ? meta.EntityName : target.ToString()),
                ("uid", EntityManager.GetNetEntity(target).ToString()),
                ("seconds", Math.Max(0, (int) (now - sweep.InterdictSince).TotalSeconds))));
        }
    }

    /// <summary>Why no node stands and when one will: the switch that is off, the pause, a deployment out, or the minutes to the founding.</summary>
    private string WhyNoNode(WGAdsCampaignPrototype campaign, WGAdsCampaignComponent? state, TimeSpan now)
    {
        switch (_campaign.Idle())
        {
            case WGAdsIdle.CampaignOff:
                return Loc.GetString("cmd-wg_ads-status-mode-off", ("cvar", AdsIncursionCVars.Enabled.Name));
            case WGAdsIdle.EncountersOff:
                return Loc.GetString("cmd-wg_ads-status-switch-off", ("cvar", EncountersCVars.Enabled.Name));
            case WGAdsIdle.SectorControlOff:
                return Loc.GetString("cmd-wg_ads-status-switch-off", ("cvar", SectorControlCVars.Enabled.Name));
            case WGAdsIdle.NotInRound:
                return Loc.GetString("cmd-wg_ads-status-not-in-round");
        }

        if (_campaign.Paused)
            return Loc.GetString("cmd-wg_ads-status-paused");

        if (_campaign.RunnerPaused)
            return Loc.GetString("cmd-wg_ads-status-runner-paused");

        if (_campaign.Deployments().Count > 0)
            return Loc.GetString("cmd-wg_ads-status-deployment-out");

        if (_campaign.FoundingDue(campaign, state) is not { } due)
            return Loc.GetString("cmd-wg_ads-status-founding-untimed");

        if (due <= now)
            return Loc.GetString("cmd-wg_ads-status-founding-overdue");

        var minutes = (int) Math.Ceiling((due - now).TotalMinutes);
        return Loc.GetString(state is { Founded: > 0 } ? "cmd-wg_ads-status-regroup-due" : "cmd-wg_ads-status-first-due",
            ("minutes", minutes));
    }

    /// <summary>Where an encounter's first ship is, or the entity itself if it is no encounter.</summary>
    private System.Numerics.Vector2? Lead(EntityUid uid, SharedTransformSystem transform)
    {
        if (!EntityManager.TryGetComponent<WGEncounterComponent>(uid, out var encounter))
            return transform.GetWorldPosition(uid);

        foreach (var ship in encounter.Ships.Values)
        {
            if (EntityManager.EntityExists(ship.Grid))
                return transform.GetWorldPosition(ship.Grid);
        }

        return null;
    }

    /// <summary>wg_ads dreadnought: activates the dreadnought now, whatever the tier and delay; one at a time.</summary>
    private void Dreadnought(IConsoleShell shell)
    {
        if (!_campaign.TryLaunchDreadnought(out var dreadnought))
        {
            Failed(shell, "cmd-wg_ads-dreadnought-failed");
            return;
        }

        var target = EntityManager.GetComponent<WGAdsDreadnoughtComponent>(dreadnought).Target;
        shell.WriteLine(Loc.GetString("cmd-wg_ads-dreadnought",
            ("uid", EntityManager.GetNetEntity(dreadnought).ToString()),
            ("target", target is { } aim && EntityManager.TryGetComponent<MetaDataComponent>(aim, out var meta) ? meta.EntityName : "-")));
    }

    /// <summary>Seconds until a time, or a dash when there is none.</summary>
    private static string Seconds(TimeSpan? at, TimeSpan now)
    {
        return at is { } time ? Math.Max(0, (int) (time - now).TotalSeconds).ToString(CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>wg_ads found [x y]: where the rules put it, or at a point on the sector map.</summary>
    private void Found(IConsoleShell shell, string[] args)
    {
        EntityUid node;
        bool founded;
        if (args.Length == 3)
        {
            if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !float.IsFinite(x) || !float.IsFinite(y))
            {
                shell.WriteLine(Loc.GetString("cmd-wg_ads-bad-position", ("x", args[1]), ("y", args[2])));
                return;
            }

            founded = _campaign.TryFoundAt(new MapCoordinates(x, y, _territory.SectorMap), out node);
        }
        else
        {
            founded = _campaign.TryFound(out node);
        }

        if (!founded)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-found-failed"));
            return;
        }

        var cell = _territory.CellOf(EntityManager.System<SharedTransformSystem>().GetMapCoordinates(node));
        shell.WriteLine(Loc.GetString("cmd-wg_ads-founded",
            ("uid", EntityManager.GetNetEntity(node).ToString()),
            ("callsign", WGSectorHex.Callsign(cell))));
    }

    /// <summary>wg_ads deploy: sends a node out to the next cell, as the campaign does.</summary>
    private void Deploy(IConsoleShell shell)
    {
        if (!_campaign.TryLaunchDeployment(out var deployment)
            || !EntityManager.TryGetComponent<WGAdsDeploymentComponent>(deployment, out var comp))
        {
            Failed(shell, "cmd-wg_ads-deploy-failed");
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_ads-deploy",
            ("uid", EntityManager.GetNetEntity(deployment).ToString()),
            ("callsign", WGSectorHex.Callsign(comp.Target))));
    }

    /// <summary>wg_ads sentinel &lt;node uid&gt;: sends a node a sentinel now.</summary>
    private void Sentinel(IConsoleShell shell, string arg)
    {
        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-no-campaign"));
            return;
        }

        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var node)
            || !_campaign.Nodes().Any(entry => entry.Owner == node))
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-bad-node", ("arg", arg)));
            return;
        }

        if (!_campaign.TrySentinel(node.Value)
            || EntityManager.GetComponent<WGAdsNodeComponent>(node.Value).Inbound is not { } inbound)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-sentinel-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_ads-sentinel",
            ("uid", EntityManager.GetNetEntity(inbound).ToString()),
            ("callsign", WGSectorHex.Callsign(EntityManager.GetComponent<WGAdsNodeComponent>(node.Value).Cell))));
    }

    /// <summary>wg_ads tier &lt;n&gt;: forces the tier.</summary>
    private void Tier(IConsoleShell shell, string arg)
    {
        if (!int.TryParse(arg, out var tier) || tier < 0 || tier > WGAdsCampaignSystem.MaxTier)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-bad-tier", ("arg", arg), ("max", WGAdsCampaignSystem.MaxTier)));
            return;
        }

        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-no-campaign"));
            return;
        }

        _campaign.SetTier(tier);
        shell.WriteLine(Loc.GetString("cmd-wg_ads-tier-set", ("tier", _campaign.TierName(tier))));
    }

    /// <summary>wg_ads sweep: from the node with the fewest sweeps out.</summary>
    private void Sweep(IConsoleShell shell)
    {
        var sweeps = _campaign.Sweeps();
        foreach (var node in _campaign.Nodes().OrderBy(entry => sweeps.Count(sweep => sweep.Comp1.Node == entry.Owner)))
        {
            if (!_campaign.TryLaunchSweep(node.Owner))
                continue;

            shell.WriteLine(Loc.GetString("cmd-wg_ads-sweep", ("callsign", WGSectorHex.Callsign(node.Comp1.Cell))));
            return;
        }

        Failed(shell, "cmd-wg_ads-sweep-failed");
    }

    /// <summary>wg_ads interdict &lt;grid uid&gt;: sends a sweep after a ship now.</summary>
    private void Interdict(IConsoleShell shell, string arg)
    {
        if (!NetEntity.TryParse(arg, out var net) || !EntityManager.TryGetEntity(net, out var grid)
            || !EntityManager.HasComponent<MapGridComponent>(grid))
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-bad-grid", ("arg", arg)));
            return;
        }

        if (_campaign.Campaign == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_ads-no-campaign"));
            return;
        }

        var name = EntityManager.GetComponent<MetaDataComponent>(grid.Value).EntityName;
        shell.WriteLine(Loc.GetString(_campaign.TryInterdict(grid.Value) ? "cmd-wg_ads-interdict" : "cmd-wg_ads-interdict-failed",
            ("ship", name)));
    }

    /// <summary>wg_ads challenge [q r]: the cell the rules pick, or a given one.</summary>
    private void Challenge(IConsoleShell shell, string[] args)
    {
        EntityUid interdictor;
        bool sent;
        if (args.Length == 3)
        {
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
                || !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            {
                shell.WriteLine(Loc.GetString("cmd-wg_ads-bad-cell", ("q", args[1]), ("r", args[2])));
                return;
            }

            sent = _campaign.TryChallenge(new WGSectorCell(q, r), out interdictor);
        }
        else
        {
            sent = _campaign.TryChallenge(out interdictor);
        }

        if (!sent || !EntityManager.TryGetComponent<WGAdsInterdictorComponent>(interdictor, out var comp))
        {
            Failed(shell, "cmd-wg_ads-challenge-failed");
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_ads-challenge",
            ("uid", EntityManager.GetNetEntity(interdictor).ToString()),
            ("callsign", WGSectorHex.Callsign(comp.Target))));
    }

    /// <summary>Prints why a step failed: no campaign, or no node standing and the way to place one, else the step's own line.</summary>
    private void Failed(IConsoleShell shell, string key)
    {
        if (_campaign.Campaign == null)
            key = "cmd-wg_ads-no-campaign";
        else if (_campaign.Nodes().Count == 0)
            key = "cmd-wg_ads-no-node";

        shell.WriteLine(Loc.GetString(key));
    }
}
