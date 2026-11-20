using Content.Server._WG.Ordnance;
using Content.Server._WG.Remade.Monolith;
using Content.Server._WG.Remade.Siege;
using Content.Shared._WG.Ghost;
using Content.Shared._WG.Warheads;

namespace Content.Server._WG.Ghost;

// The game loop's own live events: Scions, warheads, planted drop beacons and the Choir's flesh ships.
public sealed partial class GhostOrbitSystem
{
    private void AddGameModeInterests(List<GhostOrbitTarget> targets, HashSet<EntityUid> seen, bool isAdmin)
    {
        AddInterest<WGWarheadComponent>(targets, seen, isAdmin, "wg-ghost-orbit-interest-warhead");
        AddInterest<WGOrdnanceBeaconComponent>(targets, seen, isAdmin, "wg-ghost-orbit-interest-beacon");
        AddInterest<WGMonolithComponent>(targets, seen, isAdmin, "wg-ghost-orbit-interest-scion");
        AddInterest<WGNecroticShipComponent>(targets, seen, isAdmin, "wg-ghost-orbit-interest-flesh-ship");
    }

    /// <summary>The state line for a game-mode event: a Scion's stage or a warhead's arming. Null for anything else.</summary>
    private string? GameModeDetail(EntityUid uid)
    {
        if (TryComp<WGMonolithComponent>(uid, out var monolith))
        {
            var stage = Loc.GetString($"wg-monolith-stage-{monolith.Stage.ToString().ToLowerInvariant()}");
            return Loc.GetString("wg-ghost-orbit-interest-scion-stage", ("stage", stage));
        }

        if (TryComp<WGWarheadComponent>(uid, out var warhead))
            return Loc.GetString($"wg-ghost-orbit-interest-warhead-{warhead.State.ToString().ToLowerInvariant()}");

        return null;
    }
}
