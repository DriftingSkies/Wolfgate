using Content.Server._NF.CryoSleep;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Settlers: a player in the lobby who owns a live outpost with a free cryopod can join the round in it, mid-round, as
/// the outpost settler, with that job's loadout and title from their character. They join its crew in Crew Setup too.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private WFRoundSetupSystem _roundSetup = default!;

    /// <summary>Cryopods an outpost holds at most.</summary>
    public const int MaxPods = 6;

    /// <summary>The job an outpost's owner joins as.</summary>
    public static readonly ProtoId<JobPrototype> SettlerJob = WFOutpostLimits.SettlerJob;

    private static readonly EntProtoId SettlerStation = "WFOutpostStation";

    private EntityUid? _settlerStation;

    private void InitializeSettlers()
    {
        SubscribeNetworkEvent<WFOutpostJoinQueryEvent>(OnJoinQuery);
        SubscribeNetworkEvent<WFOutpostJoinRequestEvent>(OnJoinRequest);
    }

    /// <summary>The cryopods standing anchored on an outpost.</summary>
    public int CountPods(EntityUid grid)
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFOutpostCryopodComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                count++;
        }

        return count;
    }

    /// <summary>Whether a pack sets up a machine with a <typeparamref name="T"/>, such as a cryopod.</summary>
    private bool Deploys<T>(Entity<WFDeployableComponent> pack) where T : IComponent, new()
    {
        if (_containers.TryGetContainer(pack, WFDeployableComponent.ContainerId, out var container)
            && container.ContainedEntities.Count > 0)
            return HasComp<T>(container.ContainedEntities[0]);

        return _proto.Index(pack.Comp.Machine).TryGetComponent<T>(out _, EntityManager.ComponentFactory);
    }

    /// <summary>A free cryopod on the account's live outpost: anchored there, empty and with nobody standing in it.</summary>
    public bool TryGetFreePod(NetUserId owner, out EntityUid pod)
    {
        pod = default;
        return TryGetOutpost(owner, out var outpost) && TryGetFreePod(outpost.Owner, out pod);
    }

    /// <summary>A free cryopod on an outpost grid: anchored there, empty and with nobody standing in it.</summary>
    public bool TryGetFreePod(EntityUid outpost, out EntityUid pod)
    {
        pod = default;
        var query = EntityQueryEnumerator<WFOutpostCryopodComponent, CryoSleepComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var cryo, out var xform))
        {
            if (xform.GridUid != outpost || !xform.Anchored || cryo.BodyContainer.ContainedEntity != null
                || _lookup.GetEntitiesInRange<MobStateComponent>(xform.Coordinates, 0.4f).Count > 0)
                continue;

            pod = uid;
            return true;
        }

        return false;
    }

    /// <summary>The name of the outpost a player in the lobby can join the round at, or null if there is none.</summary>
    public string? GetJoinOffer(ICommonSession session)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound
            || _ticker.PlayerGameStatuses.GetValueOrDefault(session.UserId) == PlayerGameStatus.JoinedGame
            || !TryGetOutpost(session.UserId, out var outpost) || !TryGetFreePod(session.UserId, out _))
            return null;

        return Name(outpost);
    }

    /// <summary>
    /// Joins a player in the lobby to the round in a free cryopod on their outpost, as its settler. <paramref name="reason"/>
    /// is a loc id when it fails.
    /// </summary>
    public bool TryJoin(ICommonSession session, out string reason)
    {
        if (_roundSetup.IsInCrew(session.UserId))
        {
            reason = "wf-outpost-join-in-crew";
            return false;
        }

        if (!TryGetOutpost(session.UserId, out var outpost))
        {
            reason = "wf-outpost-join-no-outpost";
            return false;
        }

        if (!TryGetFreePod(session.UserId, out var pod))
        {
            reason = "wf-outpost-join-no-pod";
            return false;
        }

        if (!_roundSetup.TrySpawnAt(session, EnsureSettlerStation(), SettlerJob, Transform(pod).Coordinates))
        {
            reason = "wf-outpost-join-failed";
            return false;
        }

        _roundSetup.JoinOutpostCrew(session, outpost, Name(outpost));
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{session:player} joined the round at outpost {ToPrettyString(outpost):grid}");
        reason = string.Empty;
        return true;
    }

    /// <summary>The station settlers and Outpost crews join as, made the first time one does in a round.</summary>
    public EntityUid EnsureSettlerStation()
    {
        if (_settlerStation is { } existing && !TerminatingOrDeleted(existing))
            return existing;

        var station = _stations.InitializeNewStation(new StationConfig
        {
            StationPrototype = SettlerStation,
            StationComponentOverrides = new ComponentRegistry(),
        }, null, Loc.GetString("wf-outpost-settler-station"));
        _settlerStation = station;
        return station;
    }

    private void OnJoinQuery(WFOutpostJoinQueryEvent msg, EntitySessionEventArgs args)
    {
        RaiseNetworkEvent(new WFOutpostJoinOfferEvent(GetJoinOffer(args.SenderSession)), args.SenderSession);
    }

    private void OnJoinRequest(WFOutpostJoinRequestEvent msg, EntitySessionEventArgs args)
    {
        if (!TryJoin(args.SenderSession, out var reason))
        {
            _chat.DispatchServerMessage(args.SenderSession, Loc.GetString(reason));
            RaiseNetworkEvent(new WFOutpostJoinOfferEvent(GetJoinOffer(args.SenderSession)), args.SenderSession);
        }
    }
}
