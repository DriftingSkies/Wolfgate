using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WG.NetTraffic;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.NetTraffic;

/// <summary>
/// Sends a ship's repair snapshot only to players who hold or wear a repair tool. The snapshot lists every tile and
/// fixture of the ship and is several times the size of the grid itself, and only the tool's preview reads it.
/// </summary>
public sealed partial class WGRepairDataNetSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long a player keeps getting snapshots after putting a tool away, so moving it between hands or
    /// slots doesn't resend every ship.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    private bool _toolOnly;

    /// <summary>Players who put their last tool away, and when.</summary>
    private readonly Dictionary<ICommonSession, TimeSpan> _putAway = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipRepairDataComponent, ComponentGetStateAttemptEvent>(OnGetStateAttempt);
        SubscribeLocalEvent<RepairDataEyeComponent, ComponentStartup>(OnToolUserStartup);
        SubscribeLocalEvent<RepairDataEyeComponent, ComponentShutdown>(OnToolUserShutdown);
        Subs.CVar(_cfg, NetTrafficCVars.RepairDataToolOnly, OnToolOnlyChanged, true);
    }

    private void OnGetStateAttempt(Entity<ShipRepairDataComponent> ent, ref ComponentGetStateAttemptEvent args)
    {
        // No player is a replay recording, which keeps everything.
        if (!_toolOnly || args.Cancelled || args.Player is not { } player)
            return;

        if (!HasComp<RepairDataEyeComponent>(player.AttachedEntity) && !InGrace(player))
            args.Cancelled = true;
    }

    /// <summary>A player has just picked a tool up, and has none of the snapshots they were not sent before.</summary>
    private void OnToolUserStartup(Entity<RepairDataEyeComponent> ent, ref ComponentStartup args)
    {
        if (!_toolOnly || !TryComp<ActorComponent>(ent, out var actor))
            return;

        // Still within the grace period, the player never stopped getting snapshots.
        var inGrace = InGrace(actor.PlayerSession);
        _putAway.Remove(actor.PlayerSession);
        if (!inGrace)
            ResendAll();
    }

    private void OnToolUserShutdown(Entity<RepairDataEyeComponent> ent, ref ComponentShutdown args)
    {
        if (!_toolOnly || !TryComp<ActorComponent>(ent, out var actor))
            return;

        var now = _timing.CurTime;
        foreach (var (session, at) in _putAway)
        {
            if (now - at >= Grace)
                _putAway.Remove(session);
        }

        _putAway[actor.PlayerSession] = now;
    }

    // Read only: state attempts can run on the PVS worker threads.
    private bool InGrace(ICommonSession session)
    {
        return _putAway.TryGetValue(session, out var at) && _timing.CurTime - at < Grace;
    }

    private void OnToolOnlyChanged(bool value)
    {
        _toolOnly = value;
        _putAway.Clear();
        if (!value)
            ResendAll();
    }

    /// <summary>Marks every snapshot changed, so each goes again to whoever is now allowed it.</summary>
    private void ResendAll()
    {
        var query = EntityQueryEnumerator<ShipRepairDataComponent>();
        while (query.MoveNext(out var uid, out var data))
        {
            Dirty(uid, data);
        }
    }
}
