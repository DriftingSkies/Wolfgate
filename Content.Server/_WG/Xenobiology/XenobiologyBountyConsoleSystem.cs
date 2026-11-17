using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Research.Systems;
using Content.Shared._WG.Xenobiology.Bounties;
using Content.Shared._WG.Xenobiology.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Research.Components;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Server._WG.Xenobiology;

/// <summary>
/// A research server's xenobiology bounty board, shared by every bounty console linked to it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class XenobiologyBountyBoardComponent : Component
{
    [DataField]
    public List<XenobiologyBountyData> Bounties = new();

    [DataField]
    public List<XenobiologyBountyHistoryData> History = new();

    /// <summary>
    /// Bounties a refresh puts on the board.
    /// </summary>
    [DataField]
    public int BoardSize = 5;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextSkipTime = TimeSpan.Zero;

    [DataField]
    public TimeSpan SkipDelay = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan RefreshDelay = TimeSpan.FromMinutes(12);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextRefresh = TimeSpan.Zero;
}

/// <summary>
/// Xenobiology bounty consoles: each research server keeps one board of a few random bounties, redrawn on a timer, and
/// is paid research points when one is handed in at any of its consoles.
/// </summary>
public sealed partial class XenobiologyBountyConsoleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenobiologyBountyConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<XenobiologyBountyConsoleComponent, XenobiologyBountyFulfillMessage>(OnFulfill);
        SubscribeLocalEvent<XenobiologyBountyConsoleComponent, XenobiologyBountySkipMessage>(OnSkip);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenobiologyBountyBoardComponent, ResearchServerComponent>();
        while (query.MoveNext(out _, out var board, out var server))
        {
            if (board.NextRefresh > now)
                continue;

            Refresh(board);
            UpdateConsoles(server);
        }
    }

    private void OnOpened(Entity<XenobiologyBountyConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateState(ent);
    }

    private void OnFulfill(Entity<XenobiologyBountyConsoleComponent> ent, ref XenobiologyBountyFulfillMessage args)
    {
        if (!TryGetBoard(ent, out var server, out var board))
        {
            Deny(ent);
            return;
        }

        if (!TryGetBounty(board, args.BountyId, out var bounty)
            || !ProtoMan.TryIndex(bounty.Bounty, out var proto)
            || proto.PointsAwarded <= 0)
            return;

        if (!IsBountyComplete(args.Actor, proto.Entries, out var handedIn))
        {
            Deny(ent);
            return;
        }

        foreach (var item in handedIn)
        {
            QueueDel(item);
        }

        _research.ModifyServerPoints(server.Value, (int) proto.PointsAwarded, server.Value.Comp);
        RemoveBounty(board, bounty, XenobiologyBountyResult.Completed, args.Actor);
        _audio.PlayPvs(ent.Comp.FulfillSound, ent);
        UpdateConsoles(server.Value.Comp);
    }

    private void OnSkip(Entity<XenobiologyBountyConsoleComponent> ent, ref XenobiologyBountySkipMessage args)
    {
        if (!TryGetBoard(ent, out var server, out var board))
            return;

        if (TryComp<AccessReaderComponent>(ent, out var reader) && !_access.IsAllowed(args.Actor, ent, reader))
        {
            Deny(ent);
            return;
        }

        if (TrySkip(board, args.BountyId, args.Actor))
            UpdateConsoles(server.Value.Comp);
    }

    private void Deny(Entity<XenobiologyBountyConsoleComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.NextDenySoundTime)
            return;

        ent.Comp.NextDenySoundTime = _timing.CurTime + ent.Comp.DenySoundDelay;
        _audio.PlayPvs(ent.Comp.DenySound, ent);
    }

    /// <summary>
    /// The board of the research server a console is linked to, drawn the first time it is asked for.
    /// </summary>
    public bool TryGetBoard(EntityUid console, [NotNullWhen(true)] out Entity<ResearchServerComponent>? server,
        [NotNullWhen(true)] out XenobiologyBountyBoardComponent? board)
    {
        server = null;
        board = null;
        if (!_research.TryGetClientServer(console, out var serverUid, out var serverComp))
            return false;

        server = new Entity<ResearchServerComponent>(serverUid.Value, serverComp);
        if (TryComp(serverUid, out board))
            return true;

        board = AddComp<XenobiologyBountyBoardComponent>(serverUid.Value);
        Refresh(board);
        return true;
    }

    /// <summary>
    /// Swaps one bounty for another the board doesn't show; bounties already handed in stay gone until the refresh.
    /// </summary>
    public bool TrySkip(XenobiologyBountyBoardComponent board, string bountyId, EntityUid actor)
    {
        if (_timing.CurTime < board.NextSkipTime || !TryGetBounty(board, bountyId, out var bounty))
            return false;

        RemoveBounty(board, bounty, XenobiologyBountyResult.Skipped, actor);
        AddRandomBounty(board, bounty.Bounty);
        SortBounties(board);
        board.NextSkipTime = _timing.CurTime + board.SkipDelay;
        return true;
    }

    /// <summary>
    /// Redraws the whole board and restarts its timer.
    /// </summary>
    private void Refresh(XenobiologyBountyBoardComponent board)
    {
        board.NextRefresh = _timing.CurTime + board.RefreshDelay;
        board.Bounties.Clear();
        for (var i = 0; i < board.BoardSize; i++)
        {
            if (!AddRandomBounty(board))
                break;
        }

        SortBounties(board);
    }

    /// <summary>
    /// Adds a random bounty the board doesn't show, other than <paramref name="except"/>; false if none is left.
    /// </summary>
    private bool AddRandomBounty(XenobiologyBountyBoardComponent board, ProtoId<XenobiologyBountyPrototype>? except = null)
    {
        var pool = ProtoMan.EnumeratePrototypes<XenobiologyBountyPrototype>()
            .Where(p => p.PointsAwarded > 0 && p.ID != except?.Id && board.Bounties.All(b => b.Bounty != p.ID))
            .OrderBy(p => p.ID)
            .ToList();
        if (pool.Count == 0)
            return false;

        var proto = _random.Pick(pool);
        int number;
        do
        {
            number = _random.Next(1000);
        } while (board.Bounties.Any(b => b.Id == $"{proto.IdPrefix}{number:D3}"));

        board.Bounties.Add(new XenobiologyBountyData(proto, number));
        return true;
    }

    private void SortBounties(XenobiologyBountyBoardComponent board)
    {
        board.Bounties = board.Bounties
            .OrderBy(b => ProtoMan.TryIndex(b.Bounty, out var p) ? p.PointsAwarded : 0)
            .ToList();
    }

    private void RemoveBounty(XenobiologyBountyBoardComponent board, XenobiologyBountyData bounty,
        XenobiologyBountyResult result, EntityUid actor)
    {
        var identity = new TryGetIdentityShortInfoEvent(null, actor);
        RaiseLocalEvent(identity);
        board.History.Add(new XenobiologyBountyHistoryData(bounty, result, _timing.CurTime, identity.Title));
        board.Bounties.Remove(bounty);
    }

    private static bool TryGetBounty(XenobiologyBountyBoardComponent board, string id,
        [NotNullWhen(true)] out XenobiologyBountyData? bounty)
    {
        bounty = board.Bounties.FirstOrDefault(b => b.Id == id);
        return bounty != null;
    }

    /// <summary>
    /// Whether the actor carries everything the bounty asks for, and which items those are. Spent extracts don't count.
    /// </summary>
    public bool IsBountyComplete(EntityUid actor, IEnumerable<XenobiologyBountyItemEntry> entries, out HashSet<EntityUid> handedIn)
    {
        var available = new HashSet<EntityUid>();
        CollectContents(actor, available);
        available.Remove(actor);

        handedIn = new HashSet<EntityUid>();
        foreach (var entry in entries)
        {
            var count = 0;
            var picked = new List<EntityUid>();
            foreach (var item in available)
            {
                if (!_whitelist.IsValid(entry.Whitelist, item)
                    || entry.Blacklist != null && _whitelist.IsValid(entry.Blacklist, item)
                    || TryComp<SlimeExtractComponent>(item, out var extract) && extract.Spent)
                    continue;

                count += CompOrNull<StackComponent>(item)?.Count ?? 1;
                picked.Add(item);
                if (count >= entry.Amount)
                    break;
            }

            if (count < entry.Amount)
                return false;

            foreach (var item in picked)
            {
                available.Remove(item);
                handedIn.Add(item);
            }
        }

        return true;
    }

    private void CollectContents(EntityUid uid, HashSet<EntityUid> into)
    {
        into.Add(uid);
        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
            {
                CollectContents(child, into);
            }
        }
    }

    /// <summary>
    /// Sends the board to every bounty console linked to the server.
    /// </summary>
    private void UpdateConsoles(ResearchServerComponent server)
    {
        foreach (var client in server.Clients)
        {
            if (HasComp<XenobiologyBountyConsoleComponent>(client))
                UpdateState(client);
        }
    }

    private void UpdateState(EntityUid console)
    {
        var now = _timing.CurTime;
        var state = TryGetBoard(console, out _, out var board)
            ? new XenobiologyBountyConsoleState(board.Bounties, board.History, board.NextSkipTime - now, board.NextRefresh - now)
            : new XenobiologyBountyConsoleState(new(), new(), TimeSpan.Zero, TimeSpan.Zero);
        _ui.SetUiState(console, XenobiologyBountyUiKey.Key, state);
    }
}
