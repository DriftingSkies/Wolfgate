using Content.Shared._WG.Drydock;

namespace Content.Server._WG.Drydock;

/// <summary>
/// Walk-in berth lights: a console can light its whole berth work area for a while, so a pilot can line up without a booking.
/// </summary>
public sealed partial class WGDrydockSystem
{
    private void InitializeBerthLights()
    {
        SubscribeLocalEvent<WGDrydockConsoleComponent, WGDrydockToggleBerthLightsMessage>(OnToggleBerthLights);
    }

    private void OnToggleBerthLights(EntityUid uid, WGDrydockConsoleComponent comp, WGDrydockToggleBerthLightsMessage args)
    {
        if (WalkInLocked(uid, comp, args.Actor))
            return;

        if (comp.BerthLightsUntil != null)
        {
            TurnOffBerthLights(uid, comp);
            UpdateUi(uid, comp);
            return;
        }

        if (HasComp<WGDrydockJobComponent>(uid))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-busy"));
            return;
        }

        if (!TryGetBerth(uid, comp, out var berth))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-berth-lights-no-berth"));
            return;
        }

        var guide = EnsureComp<WGDrydockGuideComponent>(uid);
        FillGuide(guide, berth, comp.ApproachLength, berth.Area.Size);
        Dirty(uid, guide);
        comp.BerthLightsUntil = _timing.CurTime + TimeSpan.FromSeconds(comp.BerthLightsTime);
        UpdateUi(uid, comp);
    }

    /// <summary>Removes the console's berth guide and clears its expiry.</summary>
    private void TurnOffBerthLights(EntityUid console, WGDrydockConsoleComponent comp)
    {
        comp.BerthLightsUntil = null;
        RemComp<WGDrydockGuideComponent>(console);
    }

    /// <summary>Switches off berth lights whose time has run out.</summary>
    private void UpdateBerthLights(TimeSpan curTime)
    {
        var query = EntityQueryEnumerator<WGDrydockConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.BerthLightsUntil is not { } until || curTime < until)
                continue;

            TurnOffBerthLights(uid, comp);
            UpdateUi(uid, comp);
        }
    }
}
