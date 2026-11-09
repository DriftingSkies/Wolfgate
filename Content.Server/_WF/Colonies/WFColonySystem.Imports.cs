using System.Linq;
using Content.Server._WF.Contracts;
using Content.Server._WF.TradeGoods;
using Content.Server._WF.TradeGoods.Components;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.TradeGoods;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Colonies;

/// <summary>
/// A colony's imports. The slow channel: what the sector owes it comes down a few crates at a time as drops on a trade hub,
/// stopped by a blockade. The fast channel: haulers selling at its exchange, which scores for their holder.
/// </summary>
public sealed partial class WFColonySystem
{
    /// <summary>The pod an import comes down in; it only shows the delivery, the crates go straight onto the shelf.</summary>
    public static readonly EntProtoId ImportPod = "WFColonyImportPod";

    /// <summary>The contractors a colony short of a good posts as, with that good.</summary>
    private static readonly (string Contractor, string Good)[] ShortageContractors =
    {
        ("WFContractorColonySupplies", Supplies),
        ("WFContractorColonyMedical", Medical),
    };

    private void InitializeImports()
    {
        SubscribeLocalEvent<WFTradeSaleEvent>(OnSale);
        SubscribeLocalEvent<WFContractorHomesEvent>(OnContractorHomes);
    }

    /// <summary>The colony shortest of a good under half its target is home to that good's contractor on the public board.</summary>
    private void OnContractorHomes(WFContractorHomesEvent args)
    {
        foreach (var (contractor, good) in ShortageContractors)
        {
            EntityUid? shortest = null;
            var lowest = 0.5f;
            var query = EntityQueryEnumerator<WFColonyComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                var level = _market.Level(uid, good);
                if (level >= lowest)
                    continue;

                lowest = level;
                shortest = uid;
            }

            if (shortest is { } home)
                args.Homes[contractor] = home;
        }
    }

    private void UpdateImports(Entity<WFColonyComponent> colony, List<OutpostEntry> entries)
    {
        var blockaded = _sector.IsBlockaded(colony);
        if (blockaded != colony.Comp.Blockaded)
            _market.Radio(Loc.GetString(blockaded ? "wf-colony-blockaded" : "wf-colony-blockade-lifted", ("world", Name(colony))));
        colony.Comp.Blockaded = blockaded;

        // A supply run brings the imports while one can fly; with runs off, or a world off the sector map, they just come down.
        if (CanFlySupply(colony))
        {
            TendSupplyRun(colony, entries);
            return;
        }

        var now = _timing.CurTime;
        if (now < colony.Comp.NextImport)
            return;

        colony.Comp.NextImport = now + TimeSpan.FromSeconds(_cfg.GetCVar(ColonyCVars.ImportInterval));
        if (!colony.Comp.Blockaded)
            DropImports(colony, entries, _cfg.GetCVar(ColonyCVars.ImportCrates));
    }

    /// <summary>The hub a colony's imports come down at: a holder's ready one, else a public one on its ground.</summary>
    private EntityUid? ImportHub(Entity<WFColonyComponent> colony, List<OutpostEntry> entries, out EntityUid? outpost)
    {
        outpost = null;
        if (ReadyHub(entries, null) is { } ready && _outposts.GetHubStatus((ready.Owner, ready.Comp1), out var hub) == WFOutpostHubStatus.Ready)
        {
            outpost = ready.Owner;
            return hub;
        }

        return TryComp<Planets.WFPlanetNetworkComponent>(colony.Comp.Network, out var network) ? _outposts.PublicHub(network.GroundMap) : null;
    }

    /// <summary>
    /// Brings down up to <paramref name="crates"/> of what the colony is owed, scarcest first, as one pod on a hub.
    /// Returns the crates that reached the shelf; none without a hub to drop on.
    /// </summary>
    private float DropImports(Entity<WFColonyComponent> colony, List<OutpostEntry> entries, float crates)
    {
        if (ImportHub(colony, entries, out var outpost) is not { } hub || !TryComp<WFTradeMarketComponent>(colony, out var market))
            return 0f;

        var delivered = 0f;
        // The scarcest goods come first, as a freighter's manifest would put them.
        foreach (var (good, row) in market.Ledger.OrderBy(pair => pair.Value.Stock).ToList())
        {
            if (crates - delivered < 1f)
                break;
            if (row.Inbound < 1f)
                continue;

            delivered += _market.Deliver(colony, good, MathF.Min(row.Inbound, crates - delivered));
        }

        if (delivered < 1f)
            return delivered;

        _outposts.DropAtHub(hub, ImportPod);
        var told = outpost is { } grid ? new[] { grid } : entries.Select(entry => entry.Outpost.Owner).ToArray();
        _market.TellAboard(told, Loc.GetString("wf-colony-import-landed", ("crates", (int) delivered)));
        return delivered;
    }

    /// <summary>A crate hauled down from off the world and sold at its colony's exchange scores for the seller's holder.</summary>
    private void OnSale(WFTradeSaleEvent args)
    {
        if (_market.MarketOf(args.Chute) is not { } market || !TryComp<WFColonyComponent>(market, out var colony)
            || !TryComp<ActorComponent>(args.Seller, out var actor)
            || !TryComp<WFTradeOriginComponent>(args.Crate, out var origin) || origin.Pending
            || origin.Point.MapId == Transform(args.Chute).MapID)
            return;

        var holder = HolderOf(actor.PlayerSession, out var name);
        AddPoints(colony.World, holder, name, 1);
    }
}
