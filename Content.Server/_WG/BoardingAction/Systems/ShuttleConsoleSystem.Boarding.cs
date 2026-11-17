using Content.Server._WG.BoardingAction;
using Content.Shared._WG.BoardingAction;
using Content.Shared.Popups;
using Robust.Shared.Audio;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private WGBoardingHelmSystem _wgBoardingHelm = default!;
    [Dependency] private WGFtlDisruptionSystem _wgDisruption = default!;

    private WGBoardingHelmState? GetWgBoardingState(EntityUid? grid) => _wgBoardingHelm.GetState(grid);

    /// <summary>Tells the pilot why a held ship won't jump.</summary>
    private bool WgRefuseDisruptedFtl(EntityUid shuttle, EntityUid console, EntityUid actor)
    {
        if (!_wgDisruption.IsGroupDisrupted(shuttle))
            return false;

        _popup.PopupEntity(Loc.GetString("wg-boarding-ftl-disrupted"), console, actor, PopupType.Medium);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg"), console);
        return true;
    }
}
