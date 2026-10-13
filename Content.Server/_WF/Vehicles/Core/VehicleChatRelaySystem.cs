using System.Linq;
using Content.Server.Chat.Systems;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared.Ghost;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using static Content.Server.Chat.Systems.ChatSystem;

namespace Content.Server._WF.Vehicles.Core;

/// <summary>
/// Carries speech and emotes through a vehicle's hull: its crew hear whoever is near the hull outside, and are heard
/// there. Whoever is looking at the hull sees the crew's speech bubbles on it.
/// </summary>
public sealed partial class VehicleChatRelaySystem : EntitySystem
{
    [Dependency] private ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        if (TryGetHull(ev.Source, out var hull))
        {
            AddListenersNear(ev, hull);

            var hullMap = Transform(hull).MapID;
            foreach (var (session, data) in ev.Recipients.ToArray())
            {
                if (session.AttachedEntity is not { } listener)
                    continue;

                // The speaker stays the author; only the bubble moves, for those who see the hull and not the cabin.
                if (TryComp(listener, out EyeComponent? eye) && eye.Target == hull ||
                    Transform(listener).MapID == hullMap)
                {
                    ev.Recipients[session] = data with { BubbleSource = hull };
                }
            }

            return;
        }

        AddListenersNear(ev, ev.Source);
    }

    /// <summary>Adds whoever is within voice range of a point, themselves or through the hull they are in.</summary>
    private void AddListenersNear(ExpandICChatRecipientsEvent ev, EntityUid point)
    {
        var coords = Transform(point).Coordinates;

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } listener || ev.Recipients.ContainsKey(session))
                continue;

            var ears = TryGetHull(listener, out var listenerHull) ? listenerHull : listener;
            if (!coords.TryDistance(EntityManager, Transform(ears).Coordinates, out var distance) ||
                distance >= ev.VoiceRange)
            {
                continue;
            }

            ev.Recipients.Add(session, new ICChatRecipientData(distance, HasComp<GhostHearingComponent>(listener)));
        }
    }

    /// <summary>The vehicle someone is inside or is working from a seat.</summary>
    private bool TryGetHull(EntityUid user, out EntityUid hull)
    {
        hull = default;

        if (TryComp(user, out VehicleInteriorOccupantComponent? occupant))
            hull = occupant.Vehicle;

        if (hull.IsValid())
            return Exists(hull) && !TerminatingOrDeleted(hull);

        if (TryComp(user, out RMCVehicleOperatorComponent? driver) && driver.Vehicle is { } driven)
            hull = driven;
        else if (TryComp(user, out VehicleWeaponsOperatorComponent? gunner) && gunner.Vehicle is { } manned)
            hull = manned;

        return hull.IsValid() && Exists(hull) && !TerminatingOrDeleted(hull);
    }
}
