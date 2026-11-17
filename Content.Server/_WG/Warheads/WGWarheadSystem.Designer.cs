using Content.Server._WG.Warheads.Components;
using Content.Shared._WG.Warheads;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Robust.Shared.Map;

namespace Content.Server._WG.Warheads;

public sealed partial class WGWarheadSystem
{
    /// <summary>Builds a warhead from a nuke designer design.</summary>
    public EntityUid SpawnDesign(WGNukeDesign design, MapCoordinates coords, EntityUid? admin)
    {
        var uid = Spawn(design.Chassis, coords);
        var warhead = EnsureComp<WGWarheadComponent>(uid);
        warhead.CustomPayload = design.Payload.Clone();
        warhead.Timer = Math.Max(design.Timer, 0f);
        warhead.YieldClass = "wg-warhead-class-custom";
        Dirty(uid, warhead);

        SetFuze<WGTimerFuzeComponent>(uid, design.Fuzes.HasFlag(WGWarheadFuzes.Timer));
        SetFuze<WGTwoPersonArmingComponent>(uid, design.Fuzes.HasFlag(WGWarheadFuzes.TwoPerson));
        SetFuze<WGImpactFuzeComponent>(uid, design.Fuzes.HasFlag(WGWarheadFuzes.Impact));

        if (design.Fuzes.HasFlag(WGWarheadFuzes.Remote))
        {
            var remote = EnsureComp<WGRemoteFuzeComponent>(uid);
            EnsureComp<DeviceLinkSinkComponent>(uid);
            _deviceLink.EnsureSinkPorts(uid, remote.Port);
        }
        else
        {
            RemComp<WGRemoteFuzeComponent>(uid);
        }

        if (!string.IsNullOrWhiteSpace(design.Name))
            _meta.SetEntityName(uid, design.Name.Trim());

        _adminLog.Add(LogType.Explosion, LogImpact.Extreme,
            $"{(admin != null ? ToPrettyString(admin.Value) : "Unknown")} spawned designed warhead {ToPrettyString(uid)} at {coords}");

        if (design.StartCountdown)
            StartCountdown((uid, warhead), admin);

        return uid;
    }

    private void SetFuze<T>(EntityUid uid, bool present) where T : Component, new()
    {
        if (present)
            EnsureComp<T>(uid);
        else
            RemComp<T>(uid);
    }
}
