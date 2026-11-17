using Content.Server.Shuttles.Events;
using Content.Shared._WG.Lattice;
using Robust.Shared.Map;

namespace Content.Server._WG.Lattice;

/// <summary>FTL drives die in bands that say so: no jumping out of them, and none into them.</summary>
public sealed partial class WGLatticeSystem
{
    private void InitializeFtl()
    {
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnFtlAttempt);
    }

    private void OnFtlAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var dead = TryComp<WGDriftComponent>(args.Uid, out var drift) && drift.NoFtl
                   || !FtlWorksAt(_transform.GetMapCoordinates(args.Uid));
        if (!dead)
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString("wg-lattice-ftl-dead");
    }

    /// <summary>Whether FTL drives work at a point; true off a map with a Lattice.</summary>
    public bool FtlWorksAt(MapCoordinates point)
    {
        return BandOf(point, out _) is not { Ftl: false };
    }
}
