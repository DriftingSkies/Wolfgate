// WOLFGATE: restore the licence header removed by Kill Spdx
// SPDX-FileCopyrightText: 2022 metalgearsloth
// SPDX-FileCopyrightText: 2023 Checkraze
// SPDX-FileCopyrightText: 2023 Leon Friedrich
// SPDX-FileCopyrightText: 2024 LordCarve
// SPDX-FileCopyrightText: 2025 Ark
// SPDX-FileCopyrightText: 2025 ark1368
//
// SPDX-License-Identifier: MPL-2.0

using Content.Server.Movement.Components;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Movement.Systems;

/// <summary>
/// Stores a buffer of previous positions of the relevant entity.
/// Can be used to check the entity's position at a recent point in time.
/// </summary>
public sealed partial class LagCompensationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    // I figured 500 ping is max, so 1.5 is 750.
    // Max ping I've had is 350ms from aus to spain.
    public static readonly TimeSpan BufferTime = TimeSpan.FromMilliseconds(750);

    // WOLFGATE(Performance) START: only entities holding recent positions are culled each tick
    private readonly HashSet<EntityUid> _wfHolding = new();
    private readonly List<EntityUid> _wfDrained = new();
    // WOLFGATE END

    public override void Initialize()
    {
        base.Initialize();
        Log.Level = LogLevel.Info;
        SubscribeLocalEvent<LagCompensationComponent, MoveEvent>(OnLagMove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var earliestTime = curTime - BufferTime;

        // Cull any old ones from active updates
        // Probably fine to include ignored.
        // WOLFGATE(Performance) START: walk only the entities that hold positions instead of every lag-compensated mob
        _wfDrained.Clear();
        foreach (var uid in _wfHolding)
        {
            if (!TryComp(uid, out LagCompensationComponent? comp))
            {
                _wfDrained.Add(uid);
                continue;
            }

            while (comp.Positions.TryPeek(out var pos))
            {
                if (pos.Item1 < earliestTime)
                {
                    comp.Positions.Dequeue();
                    continue;
                }

                break;
            }

            if (comp.Positions.Count == 0)
                _wfDrained.Add(uid);
        }

        foreach (var uid in _wfDrained)
        {
            _wfHolding.Remove(uid);
        }
        // WOLFGATE END
    }

    private void OnLagMove(EntityUid uid, LagCompensationComponent component, ref MoveEvent args)
    {
        if (!args.NewPosition.EntityId.IsValid())
            return; // probably being sent to nullspace for deletion.

        component.Positions.Enqueue((_timing.CurTime, args.NewPosition, args.NewRotation));
        _wfHolding.Add(uid); // WOLFGATE(Performance): now holds a position to cull
    }

    public (EntityCoordinates Coordinates, Angle Angle) GetCoordinatesAngle(EntityUid uid, ICommonSession? pSession,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref xform))
            return (EntityCoordinates.Invalid, Angle.Zero);

        if (pSession == null || !TryComp<LagCompensationComponent>(uid, out var lag) || lag.Positions.Count == 0)
            return (xform.Coordinates, xform.LocalRotation);

        var angle = Angle.Zero;
        var coordinates = EntityCoordinates.Invalid;
        var ping = pSession.Channel.Ping;
        // Use 1.5 due to the trip buffer.
        var sentTime = _timing.CurTime - TimeSpan.FromMilliseconds(ping * 1.5);

        foreach (var pos in lag.Positions)
        {
            coordinates = pos.Item2;
            angle = pos.Item3;

            if (pos.Item1 >= sentTime)
                break;
        }

        if (coordinates == default)
        {
            Log.Debug($"No long comp coords found, using {xform.Coordinates}");
            coordinates = xform.Coordinates;
            angle = xform.LocalRotation;
        }
        else
        {
            Log.Debug($"Actual coords is {xform.Coordinates} and got {coordinates}");
        }

        return (coordinates, angle);
    }

    public Angle GetAngle(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        var (_, angle) = GetCoordinatesAngle(uid, session, xform);
        return angle;
    }

    public EntityCoordinates GetCoordinates(EntityUid uid, ICommonSession? session, TransformComponent? xform = null)
    {
        var (coordinates, _) = GetCoordinatesAngle(uid, session, xform);
        return coordinates;
    }
}
