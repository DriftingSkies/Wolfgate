using Content.Shared._WG.Administration.AdminRadar;
using Content.Shared.Shuttles.Components;

namespace Content.Client.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    private const IFFFlags WgHidingFlags = IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways;

    /// <summary>
    /// Clears the hiding flags on every IFF for one radar draw and notes what they were. The radar code reads the
    /// flags straight off the component, so this is how the admin scanner sees past them without changing that code.
    /// Nothing is dirtied; <see cref="WgRestoreIff"/> must get the same list back before the draw returns.
    /// A grid with <see cref="WGAdminRadarUnlabelledComponent"/> is still shown but keeps its label hidden.
    /// </summary>
    public void WgRevealIff(List<(IFFComponent Iff, IFFFlags Flags)> hidden)
    {
        var query = AllEntityQuery<IFFComponent>();
        while (query.MoveNext(out var uid, out var iff))
        {
            var reveal = HasComp<WGAdminRadarUnlabelledComponent>(uid)
                ? WgHidingFlags & ~IFFFlags.HideLabelAlways
                : WgHidingFlags;
            if ((iff.Flags & reveal) == 0x0)
                continue;

            hidden.Add((iff, iff.Flags));
            iff.Flags &= ~reveal;
        }
    }

    /// <summary>
    /// Puts back the flags <see cref="WgRevealIff"/> cleared.
    /// </summary>
    public void WgRestoreIff(List<(IFFComponent Iff, IFFFlags Flags)> hidden)
    {
        foreach (var (iff, flags) in hidden)
        {
            iff.Flags = flags;
        }

        hidden.Clear();
    }
}
