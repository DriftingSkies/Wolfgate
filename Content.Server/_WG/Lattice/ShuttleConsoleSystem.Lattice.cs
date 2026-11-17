using Content.Server._WG.Lattice;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WGLatticeSystem _wgLattice = default!;

    /// <summary>Refuses a jump whose destination lies where FTL drives die, and tells the pilot.</summary>
    private bool WgRefuseLatticeTarget(EntityCoordinates target, EntityUid console, EntityUid actor)
    {
        var point = _transform.ToMapCoordinates(target);
        var ftlWorks = _wgLattice.FtlWorksAt(point);
        var scionAllowed = Transform(console).GridUid is not { } ship || _wgLattice.ScionMayJumpTo(ship, point);
        if (ftlWorks && scionAllowed)
            return false;

        var reason = ftlWorks ? "wg-lattice-hex-ftl-refused" : "wg-lattice-ftl-target-dead";
        _popup.PopupEntity(Loc.GetString(reason), console, actor, PopupType.Medium);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg"), console);
        return true;
    }
}
