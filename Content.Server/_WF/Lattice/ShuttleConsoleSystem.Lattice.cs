using Content.Server._WF.Lattice;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Map;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WFLatticeSystem _wfLattice = default!;

    /// <summary>Refuses a jump whose destination lies where FTL drives die, and tells the pilot.</summary>
    private bool WfRefuseLatticeTarget(EntityCoordinates target, EntityUid console, EntityUid actor)
    {
        var point = _transform.ToMapCoordinates(target);
        var ftlWorks = _wfLattice.FtlWorksAt(point);
        var scionAllowed = Transform(console).GridUid is not { } ship || _wfLattice.ScionMayJumpTo(ship, point);
        if (ftlWorks && scionAllowed)
            return false;

        var reason = ftlWorks ? "wf-lattice-hex-ftl-refused" : "wf-lattice-ftl-target-dead";
        _popup.PopupEntity(Loc.GetString(reason), console, actor, PopupType.Medium);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg"), console);
        return true;
    }
}
