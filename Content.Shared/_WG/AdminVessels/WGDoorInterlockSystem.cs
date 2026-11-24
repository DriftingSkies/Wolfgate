using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WG.AdminVessels;

/// <summary>
/// Cycles an interlocked pair like an airlock: a door asked to open while its partner stands open stays shut, the
/// partner closes, and the door then opens for whoever asked.
/// </summary>
public sealed class WGDoorInterlockSystem : EntitySystem
{
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long a door waits for its partner to shut before the request lapses.</summary>
    private static readonly TimeSpan CycleTime = TimeSpan.FromSeconds(5);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGDoorInterlockComponent, BeforeDoorOpenedEvent>(OnBeforeOpen);
        SubscribeLocalEvent<WGDoorInterlockComponent, DoorStateChangedEvent>(OnStateChanged);
    }

    private void OnBeforeOpen(Entity<WGDoorInterlockComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (ent.Comp.Partner is not { } partner || !TryComp<DoorComponent>(partner, out var door) || door.State == DoorState.Closed)
            return;

        args.Cancel();
        // The server runs the cycle, and only for someone at the door whom it would let through.
        if (_net.IsClient || args.User is not { } user || ent.Comp.CycleUntil > _timing.CurTime || !_doors.HasAccess(ent, user))
            return;

        ent.Comp.CycleUser = user;
        ent.Comp.CycleUntil = _timing.CurTime + CycleTime;
        // A partner still opening is closed once it is open: closing it partway would leave it passable as "closed".
        if (door.State == DoorState.Open)
            _doors.TryClose(partner, door);
        else if (door.State == DoorState.Opening && TryComp<WGDoorInterlockComponent>(partner, out var opening))
            opening.CloseWhenOpen = true;
    }

    /// <summary>
    /// A door told to close once open does so; and when a door shuts, a partner waiting to cycle opens.
    /// </summary>
    private void OnStateChanged(Entity<WGDoorInterlockComponent> ent, ref DoorStateChangedEvent args)
    {
        if (_net.IsClient)
            return;
        if (args.State == DoorState.Open && ent.Comp.CloseWhenOpen)
        {
            ent.Comp.CloseWhenOpen = false;
            _doors.TryClose(ent);
            return;
        }

        // Shut some other way before it ever got open: the deferred close is moot.
        if (args.State == DoorState.Closed)
            ent.Comp.CloseWhenOpen = false;

        if (args.State != DoorState.Closed || ent.Comp.Partner is not { } partner
            || !TryComp<WGDoorInterlockComponent>(partner, out var waiting) || waiting.CycleUntil < _timing.CurTime)
            return;

        var user = waiting.CycleUser;
        waiting.CycleUntil = TimeSpan.Zero;
        waiting.CycleUser = null;
        _doors.TryOpen(partner, user: user);
    }
}
