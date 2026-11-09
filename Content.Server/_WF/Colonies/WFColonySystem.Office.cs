using Content.Shared._WF.Colonies;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._WF.Colonies;

/// <summary>
/// The charter office: the terminal in a governed world's hall. Anyone reads the world's development on it; the
/// governing company's members work the charter there, in place of the Development tab of their outposts' consoles.
/// </summary>
public sealed partial class WFColonySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    private void InitializeOffice()
    {
        Subs.BuiEvents<WFColonyOfficeComponent>(WFColonyOfficeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOfficeOpened);
            subs.Event<WFColonyDefenceMessage>(OnOfficeDefence);
            subs.Event<WFColonyOpenMessage>(OnOfficeOpen);
            subs.Event<WFColonyPermitMessage>(OnOfficePermit);
            subs.Event<WFColonyBerthMessage>(OnOfficeBerth);
            subs.Event<WFColonyYardKitMessage>(OnOfficeYardKit);
        });
    }

    /// <summary>Whether a charter office stands on a world's ground this round, so the charter is worked from there.</summary>
    public bool HasOffice(EntityUid network)
    {
        if (!TryComp<Planets.WFPlanetNetworkComponent>(network, out var comp))
            return false;

        var query = EntityQueryEnumerator<WFColonyOfficeComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid == comp.GroundMap && xform.Anchored)
                return true;
        }

        return false;
    }

    /// <summary>The world an office stands on and the session of whoever works it.</summary>
    private bool TryOfficeWorld(EntityUid office, EntityUid actor, out string world, out EntityUid network, out ICommonSession session)
    {
        world = string.Empty;
        network = default;
        session = default!;
        if (Transform(office).MapUid is not { } map || !TryComp<ActorComponent>(actor, out var actorComp) || !TryWorldOf(map, out world, out network))
            return false;

        session = actorComp.PlayerSession;
        return true;
    }

    private void OnOfficeOpened(Entity<WFColonyOfficeComponent> office, ref BoundUIOpenedEvent args)
    {
        UpdateOffice(office, args.Actor);
    }

    private void UpdateOffice(EntityUid office, EntityUid viewer)
    {
        if (!TryOfficeWorld(office, viewer, out var world, out var network, out var session))
            return;

        var view = View(world, network, HolderOf(session, out _));
        _ui.ServerSendUiMessage(office, WFColonyOfficeUiKey.Key, new WFColonyOfficeState(view), viewer);
    }

    /// <summary>Shows every open charter office on a world afresh.</summary>
    private void RefreshOffices(string world)
    {
        var query = EntityQueryEnumerator<WFColonyOfficeComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid is not { } map || !TryWorldOf(map, out var id, out _) || id != world)
                continue;

            foreach (var actor in _ui.GetActors(uid, WFColonyOfficeUiKey.Key))
            {
                UpdateOffice(uid, actor);
            }
        }
    }

    private void OnOfficeDefence(Entity<WFColonyOfficeComponent> office, ref WFColonyDefenceMessage args)
    {
        if (TryOfficeWorld(office, args.Actor, out var world, out _, out var session) && SetDefence(world, session, args.Engage))
            RefreshConsoles(world);
    }

    private void OnOfficeOpen(Entity<WFColonyOfficeComponent> office, ref WFColonyOpenMessage args)
    {
        if (TryOfficeWorld(office, args.Actor, out var world, out _, out var session) && SetOpen(world, session, args.Open))
            RefreshConsoles(world);
    }

    private void OnOfficePermit(Entity<WFColonyOfficeComponent> office, ref WFColonyPermitMessage args)
    {
        if (TryOfficeWorld(office, args.Actor, out var world, out _, out var session) && SetPermit(world, session, args.Holder, args.Granted))
            RefreshConsoles(world);
    }

    private void OnOfficeBerth(Entity<WFColonyOfficeComponent> office, ref WFColonyBerthMessage args)
    {
        if (TryOfficeWorld(office, args.Actor, out var world, out _, out var session) && SetBerth(world, session, args.Holder, args.Open))
            RefreshConsoles(world);
    }

    /// <summary>The yard platform kit is brought out to the governor at the office, once a round while the world has no yard.</summary>
    private void OnOfficeYardKit(Entity<WFColonyOfficeComponent> office, ref WFColonyYardKitMessage args)
    {
        if (!TryOfficeWorld(office, args.Actor, out var world, out _, out var session) || !IsGovernor(world, session)
            || !_yard.MayIssueKit(world))
            return;

        Spawn(YardKit, Transform(args.Actor).Coordinates);
        _yard.KitIssued(world);
        RefreshConsoles(world);
    }
}
