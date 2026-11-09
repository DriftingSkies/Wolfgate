using System;
using System.Numerics;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Shared._WF.ShipMissiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.ShipMissiles;

/// <summary>Seeker side of a missile: what it aims at this tick, and when a beam makes it lose the lock.</summary>
public sealed partial class WFMissileFlightSystem
{
    /// <summary>
    /// The aim point for this tick: live seeker data while tracking, the last good track while notched. Returns false
    /// when there is nothing to steer on.
    /// </summary>
    private bool TryGetAim(EntityUid uid,
        TransformComponent xform,
        WFMissileFlightComponent flight,
        TargetSeekingComponent carrier,
        Vector2 ownPos,
        Vector2 ownVel,
        float frameTime,
        out Vector2 aimPos,
        out Vector2 aimVel)
    {
        aimPos = default;
        aimVel = default;

        var target = carrier.CurrentTarget;
        if (target is not { } targetUid || TerminatingOrDeleted(targetUid))
        {
            // Nothing to be blind about, and nothing to remember.
            flight.Seeker = WFMissileSeekerState.Tracking;
            flight.NotchTimer = 0f;
            flight.MemoryTimer = 0f;
            return false;
        }

        if (flight.Seeker == WFMissileSeekerState.Lost)
            return false;

        var targetXform = Transform(targetUid);
        if (targetXform.MapID != xform.MapID)
            return false;

        var targetPos = _transform.GetWorldPosition(targetXform);
        _bodies.TryGetComponent(targetUid, out var targetBody);
        var targetVel = _physics.GetMapLinearVelocity(targetUid, targetBody, targetXform);

        var relPos = targetPos - ownPos;
        var relVel = targetVel - ownVel;
        var beaming = MissileGuidance.IsBeaming(relPos, relVel, flight.NotchBand);

        if (flight.Seeker == WFMissileSeekerState.Tracking)
        {
            if (beaming)
            {
                flight.NotchTimer += frameTime;
                if (flight.NotchTimer > flight.NotchMemory)
                {
                    flight.Seeker = WFMissileSeekerState.Blind;
                    flight.MemoryTimer = 0f;
                }
            }
            else
            {
                flight.NotchTimer = MathF.Max(0f, flight.NotchTimer - 2f * frameTime);
            }

            if (flight.Seeker == WFMissileSeekerState.Tracking)
            {
                flight.KnownPosition = targetPos;
                flight.KnownVelocity = targetVel;
                if (flight.LaunchTof <= 0f)
                    flight.LaunchTof = MissileGuidance.TimeToGo(relPos, relVel);

                aimPos = targetPos;
                aimVel = targetVel;
                return true;
            }
        }

        // Notched: fly the last good track. The seeker is not seeing the target, so the track is not refreshed.
        flight.MemoryTimer += frameTime;

        if (!beaming)
        {
            // The geometry opened again: the seeker picks the target back up rather than staying dead for the flight.
            flight.Seeker = WFMissileSeekerState.Tracking;
            flight.NotchTimer = 0f;
            flight.KnownPosition = targetPos;
            flight.KnownVelocity = targetVel;
            aimPos = targetPos;
            aimVel = targetVel;
            return true;
        }

        if (flight.MemoryTimer > flight.MemoryTime)
        {
            flight.Seeker = WFMissileSeekerState.Lost;
            _targetSeeking.SetSeekerTarget((uid, carrier), null, xform);
            // Upstream would reacquire the same hull on its next tick; a lost seeker stays lost.
            carrier.SeekingDisabled = true;
            return false;
        }

        aimPos = flight.KnownPosition + flight.KnownVelocity * flight.MemoryTimer;
        aimVel = flight.KnownVelocity;
        return true;
    }
}
