using Content.Server._WF.Warheads.Components;
using Content.Shared._WF.Warheads;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Robust.Shared.Map;

namespace Content.Server._WF.Warheads;

public sealed partial class WFWarheadSystem
{
    /// <summary>Builds a warhead from a nuke designer design.</summary>
    public EntityUid SpawnDesign(WFNukeDesign design, MapCoordinates coords, EntityUid? admin)
    {
        var uid = Spawn(design.Chassis, coords);
        var warhead = EnsureComp<WFWarheadComponent>(uid);
        warhead.CustomPayload = design.Payload.Clone();
        warhead.Timer = Math.Max(design.Timer, 0f);
        warhead.YieldClass = "wf-warhead-class-custom";
        Dirty(uid, warhead);

        SetFuze<WFTimerFuzeComponent>(uid, design.Fuzes.HasFlag(WFWarheadFuzes.Timer));
        SetFuze<WFTwoPersonArmingComponent>(uid, design.Fuzes.HasFlag(WFWarheadFuzes.TwoPerson));
        SetFuze<WFImpactFuzeComponent>(uid, design.Fuzes.HasFlag(WFWarheadFuzes.Impact));

        if (design.Fuzes.HasFlag(WFWarheadFuzes.Remote))
        {
            var remote = EnsureComp<WFRemoteFuzeComponent>(uid);
            EnsureComp<DeviceLinkSinkComponent>(uid);
            _deviceLink.EnsureSinkPorts(uid, remote.Port);
        }
        else
        {
            RemComp<WFRemoteFuzeComponent>(uid);
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
