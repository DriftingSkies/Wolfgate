using System.Linq;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.CartridgeLoader;
using Content.Shared._WG.Contracts;
using Content.Shared.CartridgeLoader;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._WG.Contracts;

/// <summary>The board's window and the Contracts PDA app: what each viewer is shown, and their buttons.</summary>
public sealed partial class WGContractSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;

    public static readonly Robust.Shared.Prototypes.EntProtoId ContractsCartridge = "WGContractsCartridge";

    private void InitializeUi()
    {
        SubscribeLocalEvent<WGContractBoardComponent, BoundUIOpenedEvent>(OnBoardOpened);
        SubscribeLocalEvent<WGContractBoardComponent, WGContractRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<WGContractBoardComponent, WGContractAcceptMessage>(OnAccept);
        SubscribeLocalEvent<WGContractBoardComponent, WGContractAbandonMessage>(OnBoardAbandon);
        SubscribeLocalEvent<WGContractCartridgeComponent, CartridgeUiReadyEvent>(OnCartridgeReady);
        SubscribeLocalEvent<WGContractCartridgeComponent, CartridgeMessageEvent>(OnCartridgeMessage);
        SubscribeLocalEvent<WGPdaProgramsEvent>(OnPdaPrograms);
    }

    private void OnPdaPrograms(ref WGPdaProgramsEvent ev)
    {
        ev.Programs.Add(ContractsCartridge);
    }

    private void OnBoardOpened(Entity<WGContractBoardComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (HolderOf(args.Actor) is { } holder)
            PayOwed(holder);

        SendBoard(ent, args.Actor);
    }

    private void OnRefresh(Entity<WGContractBoardComponent> ent, ref WGContractRefreshMessage args)
    {
        SendBoard(ent, args.Actor);
    }

    private void OnAccept(Entity<WGContractBoardComponent> ent, ref WGContractAcceptMessage args)
    {
        if (TryAccept(args.Actor, args.Id, ent) is { } error)
            _popup.PopupEntity(error, ent, args.Actor);

        SendBoard(ent, args.Actor);
    }

    private void OnBoardAbandon(Entity<WGContractBoardComponent> ent, ref WGContractAbandonMessage args)
    {
        if (HolderOf(args.Actor) is { } holder)
            TryAbandon(holder, args.Id);

        SendBoard(ent, args.Actor);
    }

    private void OnCartridgeReady(Entity<WGContractCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        var holder = LoaderViewer(args.Loader);
        if (HolderOf(holder) is { } who)
            PayOwed(who);

        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, new WGContractCartridgeUiState(State(holder)));
    }

    private void OnCartridgeMessage(Entity<WGContractCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not WGContractCartridgeAbandonMessage abandon)
            return;

        var loader = GetEntity(args.LoaderUid);
        // Only the PDA's carrier may abandon from it.
        if (LoaderViewer(loader) != args.Actor || HolderOf(args.Actor) is not { } holder)
            return;

        TryAbandon(holder, abandon.Id);
        _cartridgeLoader.UpdateCartridgeUiState(loader, new WGContractCartridgeUiState(State(args.Actor)));
    }

    /// <summary>Whose contracts a PDA shows: a cyborg's built-in one is the cyborg, any other its carrier.</summary>
    private EntityUid LoaderViewer(EntityUid loader)
    {
        return HolderOf(loader) != null ? loader : Transform(loader).ParentUid;
    }

    private void SendBoard(EntityUid board, EntityUid actor, Dictionary<string, EntityUid>? homes = null)
    {
        var tag = CompOrNull<WGContractBoardComponent>(board)?.Board ?? WGContractBoards.Central;
        _ui.ServerSendUiMessage(board, WGContractBoardUiKey.Key, new WGContractBoardStateMessage(State(actor, tag, homes)), actor);
    }

    /// <summary>Sends every open board and Contracts app its viewer's state.</summary>
    private void PushAll()
    {
        Dictionary<string, EntityUid>? homes = null;
        var boards = EntityQueryEnumerator<WGContractBoardComponent>();
        while (boards.MoveNext(out var board, out _))
        {
            foreach (var actor in _ui.GetActors(board, WGContractBoardUiKey.Key).ToList())
            {
                SendBoard(board, actor, homes ??= ContractorHomes());
            }
        }

        var cartridges = EntityQueryEnumerator<WGContractCartridgeComponent, CartridgeComponent>();
        while (cartridges.MoveNext(out var uid, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is not { } loader || !TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
                || loaderComp.ActiveProgram != uid || !_ui.IsUiOpen(loader, loaderComp.UiKey))
                continue;

            _cartridgeLoader.UpdateCartridgeUiState(loader,
                new WGContractCartridgeUiState(State(LoaderViewer(loader), homes: homes ??= ContractorHomes())), loader: loaderComp);
        }
    }

    /// <summary>A viewer's board state: offers (one board's, or all), crew contracts, history and standing.</summary>
    public WGContractBoardState State(EntityUid viewer, string? board = null, Dictionary<string, EntityUid>? homes = null)
    {
        var holder = HolderOf(viewer);
        var state = new WGContractBoardState { CanHold = holder != null };
        var crew = holder is { } h ? CrewSlots(h.User) : ((int, int, int)?) null;

        foreach (var contract in _contracts.Values.Where(c => c.Status == WGContractStatus.Offered).OrderBy(c => c.Id))
        {
            if (board != null && _proto.Index(_proto.Index(contract.Prototype).Contractor).Board != board
                || !OfferedTo(contract, holder?.User))
                continue;

            var view = View(contract);
            view.Locked = Locked(holder, contract, viewer, crew);
            view.Share = Share(contract, crew?.Item1 ?? 1);
            state.Offers.Add(view);
        }

        if (holder is not { } who)
        {
            state.MaxActive = SlotsFor(1);
            state.NextSlotAt = NextSlotAt(1);
            return state;
        }

        state.CrewSize = CrewSize(who.User);
        state.MaxActive = SlotsFor(state.CrewSize);
        state.NextSlotAt = NextSlotAt(state.CrewSize);
        foreach (var contract in ActiveForCrew(who.User).OrderBy(c => c.Deadline))
        {
            var view = View(contract);
            view.Share = Share(contract, state.CrewSize);
            view.Mine = contract.User == who.User && contract.Character == who.Character;
            if (!view.Mine)
                view.Holder = contract.Character;
            state.Active.Add(view);
        }
        if (_history.TryGetValue(who, out var history))
            state.History.AddRange(history.Select(View));

        state.Owed = _owed.GetValueOrDefault(who);
        homes ??= ContractorHomes();
        foreach (var contractor in _proto.EnumeratePrototypes<WGContractorPrototype>().OrderBy(c => c.Order))
        {
            var points = Standing(who, contractor.ID);
            var rank = RankOf(points);
            state.Standing.Add(new WGContractStandingView(Loc.GetString(contractor.Name), Loc.GetString(contractor.Description),
                contractor.Color, Loc.GetString(RankName(rank)), points,
                rank + 1 < RankThresholds.Length ? RankThresholds[rank + 1] : null, homes.ContainsKey(contractor.ID)));
        }

        return state;
    }

    /// <summary>A contract as the UI shows it.</summary>
    public WGContractView View(WGContract contract)
    {
        var proto = _proto.Index(contract.Prototype);
        var contractor = _proto.Index(proto.Contractor);
        var view = new WGContractView
        {
            Id = contract.Id,
            Title = Title(contract),
            Briefing = Loc.GetString(proto.Briefing, Args(contract)),
            Contractor = Loc.GetString(contractor.Name),
            ContractorColor = contractor.Color,
            Category = proto.Category,
            Tier = proto.Tier,
            Reward = contract.Reward,
            Deposit = contract.Deposit,
            Status = contract.Status,
            Duration = contract.Duration,
            Deadline = contract.Status is WGContractStatus.Offered or WGContractStatus.Active ? contract.Deadline : null,
            MinCrew = proto.MinCrew,
            Employer = Loc.GetString("wg-contract-employer", ("contractor", Loc.GetString(contractor.Name)),
                ("employer", Loc.GetString(contractor.Employer))),
            Requires = proto.Requires.Select(r => Loc.GetString(r.Name)).ToList(),
        };

        if (proto.CrewBonus > 0)
            view.CrewBonus = Loc.GetString("wg-contract-crew-bonus", ("percent", (int) Math.Round(proto.CrewBonus * 100)),
                ("past", Math.Max(1, proto.MinCrew)), ("most", MaxBonusCrew));

        foreach (var (rival, cost) in proto.Harms)
        {
            if (_proto.TryIndex(rival, out var rivalProto))
                view.Harms.Add(Loc.GetString("wg-contract-harms", ("contractor", Loc.GetString(rivalProto.Name)), ("points", cost)));
        }

        if (proto.Credits > 0 && proto.CreditEntity is { } creditEntity && _proto.TryIndex(creditEntity, out var credit))
            view.Credits = Loc.GetString("wg-contract-credits", ("amount", proto.Credits), ("credit", credit.Name));

        var active = contract.Status == WGContractStatus.Active;
        switch (proto.Objective)
        {
            case WGContractCourier:
                view.Site = WGContractSite.Station;
                view.Location = Loc.GetString("wg-contract-location-courier",
                    ("origin", contract.Origin is { } origin ? StationName(origin) : Loc.GetString("wg-contract-board-name")),
                    ("destination", StationName(contract.Destination)), ("km", Km(contract.Distance)));
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-courier",
                    ("done", contract.Delivered), ("count", contract.Count), ("destination", StationName(contract.Destination))),
                    contract.Delivered >= contract.Count));
                if (active && contract.Origin is { } pickup
                    && contract.Items.FirstOrDefault(i => !TerminatingOrDeleted(i) && OnStation(i, pickup)) is { Valid: true } waiting)
                    view.Coordinates = _transform.GetWorldPosition(waiting);
                break;
            case WGContractSupply supply:
            {
                view.Site = WGContractSite.Station;
                view.Location = Loc.GetString("wg-contract-location-supply", ("destination", StationName(contract.Destination)),
                    ("km", Km(contract.Distance)));
                var crate = contract.Items.FirstOrDefault();
                var have = active && crate.Valid && !TerminatingOrDeleted(crate) ? Packed(contract, crate) : null;
                for (var i = 0; i < contract.Goods.Count; i++)
                {
                    var (line, count) = contract.Goods[i];
                    var packed = have?[i] ?? 0;
                    view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-goods",
                        ("name", GoodsName(supply.Goods[line])), ("have", Math.Min(packed, count)), ("count", count),
                        ("active", active)), packed >= count));
                }

                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-crate",
                    ("destination", StationName(contract.Destination))), false));
                break;
            }
            case WGContractBounty:
                view.Site = WGContractSite.Space;
                view.Location = Loc.GetString("wg-contract-location-bounty", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("active", active));
                if (active && TargetPosition(contract) is { } target)
                    view.Coordinates = target;
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-bounty",
                    ("target", TargetName(contract))), false));
                break;
            case WGContractRecovery recovery:
                view.Site = WGContractSite.Space;
                view.Location = Loc.GetString("wg-contract-location-recovery", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("destination", StationName(contract.Destination)), ("active", active));
                if (active && contract.Site is { } site)
                    view.Coordinates = site.Position;
                var item = _proto.Index(recovery.Item).Name;
                if (Math.Max(1, contract.Count) > 1)
                {
                    view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-recover-many",
                        ("item", item), ("count", contract.Count), ("destination", StationName(contract.Destination)), ("planet", recovery.Planet != null),
                        ("done", contract.Delivered)), active && contract.Delivered >= contract.Count));
                    break;
                }

                var carried = active && contract.Items.FirstOrDefault() is { Valid: true } recovered && !TerminatingOrDeleted(recovered)
                              && OffSite(contract, recovered);
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-recover", ("item", item), ("planet", recovery.Planet != null)), carried));
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-return", ("item", item),
                    ("destination", StationName(contract.Destination))), false));
                break;
            case WGContractTow tow:
            {
                view.Site = WGContractSite.Space;
                view.Location = Loc.GetString("wg-contract-location-tow", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("range", (int) tow.Range), ("active", active));
                if (active && contract.WreckGrid is { } hull && !TerminatingOrDeleted(hull))
                    view.Coordinates = _transform.GetWorldPosition(hull);
                else if (active && contract.Site is { } towSite)
                    view.Coordinates = towSite.Position;
                var gap = active ? TowGap(contract) : null;
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-tow",
                    ("destination", StationName(contract.Destination)), ("range", (int) tow.Range),
                    ("gap", gap is { } metres ? (int) metres : -1)), gap is { } close && close <= tow.Range));
                break;
            }
            case WGContractSurvey survey:
            {
                view.Site = WGContractSite.Space;
                view.Location = Loc.GetString("wg-contract-location-survey", ("home", StationName(contract.Home)),
                    ("km", Km(contract.Distance)), ("active", active));
                if (active && contract.Site is { } surveySite)
                    view.Coordinates = surveySite.Position;
                var scanned = contract.Scanned.Count;
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-survey",
                    ("done", scanned), ("count", contract.Count), ("planet", survey.Planet != null)), active && scanned >= contract.Count));
                break;
            }
            case WGContractEscort:
                view.Site = WGContractSite.Space;
                view.Location = Loc.GetString("wg-contract-location-escort", ("home", StationName(contract.Home)),
                    ("destination", StationName(contract.Destination)), ("km", Km(contract.Distance)), ("active", active));
                if (active && TargetPosition(contract) is { } escorted)
                    view.Coordinates = escorted;
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-escort-meet",
                    ("target", TargetName(contract))), active && EscortBegun(contract)));
                view.Objectives.Add(new WGContractObjectiveView(Loc.GetString("wg-contract-objective-escort",
                    ("target", TargetName(contract)), ("destination", StationName(contract.Destination))), false));
                break;
            default:
                ViewOperation(contract, proto.Objective, view, active);
                break;
        }

        if (proto.Objective.Planet != null)
        {
            var (site, world) = PlanetNames(contract);
            view.Site = WGContractSite.Planet;
            view.Location = Loc.GetString("wg-contract-location-planet", ("site", site), ("world", world),
                ("active", active && contract.PlanetSite != null));
        }

        return view;
    }

    /// <summary>What a crew of a size would be paid for a contract and each one's share, or null for a lone contractor on solo work.</summary>
    private string? Share(WGContract contract, int crewSize)
    {
        var proto = _proto.Index(contract.Prototype);
        if (crewSize <= 1 && proto.MinCrew <= 1)
            return null;

        var size = Math.Max(crewSize, proto.MinCrew);
        var pool = CrewPool(contract.Reward, proto, size);
        return Loc.GetString("wg-contract-share", ("size", size), ("pool", pool), ("each", pool / size));
    }

    public string Title(WGContract contract)
    {
        return Loc.GetString(_proto.Index(contract.Prototype).Title, Args(contract));
    }

    /// <summary>The arguments every title and briefing takes.</summary>
    private (string, object)[] Args(WGContract contract)
    {
        var proto = _proto.Index(contract.Prototype);
        var goods = proto.Objective is WGContractSupply supply
            ? string.Join(", ", contract.Goods.Select(g => Loc.GetString("wg-contract-goods-entry", ("count", g.Count),
                ("name", GoodsName(supply.Goods[g.Line])))))
            : string.Empty;
        return
        [
            ("contractor", Loc.GetString(_proto.Index(proto.Contractor).Name)),
            ("home", StationName(contract.Home)),
            ("origin", contract.Origin is { } origin ? StationName(origin) : Loc.GetString("wg-contract-board-name")),
            ("destination", StationName(contract.Destination)),
            ("count", contract.Count),
            ("goods", goods),
            ("target", TargetName(contract)),
            ("km", Km(contract.Distance)),
        ];
    }

    private static string Km(float metres) => (metres / 1000f).ToString("0.0");

    /// <summary>The bounty's target: the live encounter's name, else the encounter's own with its designation left blank.</summary>
    private string TargetName(WGContract contract)
    {
        if (contract.EncounterEntity is { } encounter && !TerminatingOrDeleted(encounter))
            return Name(encounter);

        return contract.Encounter is { } id && _proto.TryIndex(id, out var proto)
            ? Loc.GetString(proto.Name, ("designation", Loc.GetString("wg-contract-designation-unknown")))
            : string.Empty;
    }

    /// <summary>Whether an escort's ships have met their escort and set off.</summary>
    private bool EscortBegun(WGContract contract)
    {
        return contract.EncounterEntity is { } encounter
               && TryComp<Encounters.Components.WGEncounterComponent>(encounter, out var comp) && comp.Begun;
    }

    /// <summary>Where a bounty's first ship is now, else where it was placed.</summary>
    private System.Numerics.Vector2? TargetPosition(WGContract contract)
    {
        if (contract.EncounterEntity is { } encounter
            && TryComp<Encounters.Components.WGEncounterComponent>(encounter, out var comp))
        {
            foreach (var ship in comp.Ships.Values)
            {
                if (!TerminatingOrDeleted(ship.Grid))
                    return _transform.GetWorldPosition(ship.Grid);
            }
        }

        return contract.Site?.Position;
    }
}
