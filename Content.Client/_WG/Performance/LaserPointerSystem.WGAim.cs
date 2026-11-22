using Content.Shared._WG.Performance;

namespace Content.Client._Goobstation.Weapons.LaserPointer;

public sealed partial class LaserPointerSystem
{
    /// <summary>What was last sent for each wielded pointer the local player holds.</summary>
    private readonly Dictionary<EntityUid, WGAimThrottle.Sent> _wgSent = new();

    /// <summary>Whether a wielded pointer's aim is due, recording it as sent if so.</summary>
    private bool WGTakeAim(EntityUid pointer, EntityUid? hovered, System.Numerics.Vector2? dir, System.Numerics.Vector2 from)
    {
        var now = Timing.RealTime;
        WGAimThrottle.Sent? last = _wgSent.TryGetValue(pointer, out var sent) ? sent : null;
        if (!WGAimThrottle.Due(last, hovered, dir, from, now))
            return false;

        _wgSent[pointer] = new WGAimThrottle.Sent(hovered, dir, from, now);
        return true;
    }
}
