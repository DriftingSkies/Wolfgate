using System.Linq;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Colonies;

/// <summary>The outpost console's Development tab: the world's tier and holders, the outpost holder's output and the charter.</summary>
public sealed partial class WFColonySystem
{
    [Dependency] private WFColonyYardSystem _yard = default!;

    /// <summary>The packed yard platform a governor deploys in orbit.</summary>
    public const string YardKit = "WFColonyYardKit";

    private void InitializeConsole()
    {
        SubscribeLocalEvent<WFOutpostComponent, WFOutpostConsoleStateEvent>(OnConsoleState);
        Subs.BuiEvents<WFOutpostConsoleComponent>(WFOutpostConsoleUiKey.Key, subs =>
        {
            subs.Event<WFColonyKeepMessage>(OnKeepMessage);
            subs.Event<WFColonyDefenceMessage>(OnDefenceMessage);
            subs.Event<WFColonyOpenMessage>(OnOpenMessage);
            subs.Event<WFColonyPermitMessage>(OnPermitMessage);
            subs.Event<WFColonyBerthMessage>(OnBerthMessage);
            subs.Event<WFColonyYardKitMessage>(OnYardKitMessage);
        });
    }

    private void OnConsoleState(Entity<WFOutpostComponent> outpost, ref WFOutpostConsoleStateEvent args)
    {
        if (_cfg.GetCVar(ColonyCVars.Enabled) && TryWorldOf(outpost.Comp.Ground, out var world, out var network))
            args.State.Development = View(world, network, HolderOf(outpost, out _));
    }

    /// <summary>A world's development as a holder sees it.</summary>
    public WFWorldDevelopmentView View(string id, EntityUid network, string you)
    {
        var world = World(id);
        var total = world.Total;
        var governor = world.Charter?.CompanyId;
        var holders = world.Holders.Values
            .Where(holder => holder.Points >= 0.5)
            .OrderByDescending(holder => holder.Points)
            .Select(holder => HolderView(world, holder.Key, total, you))
            .ToList();

        WFWorldTier? next = world.Tier < WFWorldTier.Dominion ? world.Tier + 1 : null;
        var colony = TryComp<WFColonyComponent>(ColonyBody(network), out var comp) ? comp : null;
        var output = colony == null ? new List<WFColonyOutputView>() : Output(world, colony, you);

        WFColonyCharterView? charter = null;
        if (world.Charter is { } held)
        {
            var permits = held.Permits.Where(key => !key.StartsWith(BerthPrefix)).Select(key => HolderView(world, key, total, you)).ToList();
            var berths = held.Permits.Where(key => key.StartsWith(BerthPrefix))
                .Select(key => HolderView(world, key[BerthPrefix.Length..], total, you)).ToList();
            var applicants = world.Applicants.Select(pair => new WFWorldHolderView(pair.Key, pair.Value, 0, 0f,
                CompanyOfKey(pair.Key) != null, pair.Key == you, false)).ToList();
            charter = new WFColonyCharterView(held.Defence, held.Open, permits, applicants, berths, _yard.MayIssueKit(id));
        }

        return new WFWorldDevelopmentView(
            WorldName(id),
            world.Tier,
            total,
            next is { } tier ? Threshold(tier) : null,
            holders,
            governor is { } company ? HolderName(world, CompanyKey(company)) : null,
            (int) MathF.Round(colony?.People ?? 0f),
            colony?.Housing ?? 0,
            colony?.Blockaded ?? false,
            you,
            output,
            charter);
    }

    private WFWorldHolderView HolderView(WorldState world, string key, double total, string you)
    {
        var points = Points(world.Id, key);
        return new WFWorldHolderView(key, HolderName(world, key), points, total <= 0 ? 0f : (float) (points / total),
            CompanyOfKey(key) != null, key == you, world.Charter is { } charter && key == CompanyKey(charter.CompanyId));
    }

    /// <summary>What a holder's buildings make on a world, in crates an hour, and which goods it keeps.</summary>
    private List<WFColonyOutputView> Output(WorldState world, WFColonyComponent colony, string holder)
    {
        var rates = new Dictionary<string, float>();
        var workforce = Workforce(colony);
        foreach (var (industry, holders) in colony.Industries)
        {
            if (!holders.TryGetValue(holder, out var rate) || !_proto.TryIndex<WFTradeIndustryPrototype>(industry, out var proto))
                continue;

            foreach (var good in proto.Outputs)
            {
                rates[good] = rates.GetValueOrDefault(good) + rate * workforce;
            }
        }

        foreach (var (good, holders) in colony.Goods)
        {
            if (holders.TryGetValue(holder, out var rate))
                rates[good] = rates.GetValueOrDefault(good) + rate * workforce;
        }

        return rates
            .OrderBy(pair => pair.Key)
            .Select(pair => new WFColonyOutputView(pair.Key, _market.GoodName(pair.Key), pair.Value, Keeps(world, holder, pair.Key)))
            .ToList();
    }

    /// <summary>The world and holder a console's outpost belongs to, when the actor may act for that holder.</summary>
    private bool TryConsoleWorld(Entity<WFOutpostConsoleComponent> console, EntityUid actor, out string world,
        out string holder, out ICommonSession session)
    {
        world = string.Empty;
        holder = string.Empty;
        session = default!;
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !TryComp<ActorComponent>(actor, out var actorComp) || !TryWorldOf(outpost.Ground, out world, out _))
            return false;

        session = actorComp.PlayerSession;
        holder = HolderOf((grid, outpost), out _);
        return true;
    }

    private void OnKeepMessage(Entity<WFOutpostConsoleComponent> console, ref WFColonyKeepMessage args)
    {
        if (!TryConsoleWorld(console, args.Actor, out var world, out var holder, out var session))
            return;

        // Only the outpost's owner, or a member of the company it flies for, decides what its holder keeps.
        var outpost = Comp<WFOutpostComponent>(console.Comp.Outpost!.Value);
        if (session.UserId != outpost.Owner && (CompanyOfKey(holder) is not { } company || _companies.CompanyOfSession(session)?.Id != company))
            return;

        var state = World(world);
        if (!state.KeptGoods.TryGetValue(holder, out var goods))
            state.KeptGoods[holder] = goods = new HashSet<string>();
        if (args.Keep)
            goods.Add(args.Good);
        else
            goods.Remove(args.Good);
        _outposts.RefreshConsole((console.Comp.Outpost.Value, outpost));
    }

    private void OnDefenceMessage(Entity<WFOutpostConsoleComponent> console, ref WFColonyDefenceMessage args)
    {
        if (TryConsoleWorld(console, args.Actor, out var world, out _, out var session) && SetDefence(world, session, args.Engage))
            RefreshConsoles(world);
    }

    private void OnOpenMessage(Entity<WFOutpostConsoleComponent> console, ref WFColonyOpenMessage args)
    {
        if (TryConsoleWorld(console, args.Actor, out var world, out _, out var session) && SetOpen(world, session, args.Open))
            RefreshConsoles(world);
    }

    private void OnPermitMessage(Entity<WFOutpostConsoleComponent> console, ref WFColonyPermitMessage args)
    {
        if (TryConsoleWorld(console, args.Actor, out var world, out _, out var session)
            && SetPermit(world, session, args.Holder, args.Granted))
            RefreshConsoles(world);
    }

    private void OnBerthMessage(Entity<WFOutpostConsoleComponent> console, ref WFColonyBerthMessage args)
    {
        if (TryConsoleWorld(console, args.Actor, out var world, out _, out var session)
            && SetBerth(world, session, args.Holder, args.Open))
            RefreshConsoles(world);
    }

    /// <summary>Drops the yard platform kit at the governor's outpost, once a round while the world has no yard.</summary>
    private void OnYardKitMessage(Entity<WFOutpostConsoleComponent> console, ref WFColonyYardKitMessage args)
    {
        if (!TryConsoleWorld(console, args.Actor, out var world, out _, out var session) || !IsGovernor(world, session)
            || !_yard.MayIssueKit(world) || console.Comp.Outpost is not { } grid
            || !TryComp<WFOutpostComponent>(grid, out var outpost) || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;

        if (_outposts.DropAtHub((grid, outpost, mapGrid), YardKit) == null)
            return;

        _yard.KitIssued(world);
        RefreshConsoles(world);
    }

    /// <summary>Shows every outpost console on a world afresh.</summary>
    private void RefreshConsoles(string world)
    {
        var query = EntityQueryEnumerator<WFOutpostComponent>();
        while (query.MoveNext(out var uid, out var outpost))
        {
            if (TryWorldOf(outpost.Ground, out var id, out _) && id == world)
                _outposts.RefreshConsole((uid, outpost));
        }
    }
}
