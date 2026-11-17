using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.HTN;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// Falling back: a wounded crewman whose skill allows it breaks contact at most once a minute, ducks to a tile nearby
/// out of the hostile's sight and patches himself up with the self-care items, then rejoins the fight.
/// </summary>
public sealed class WGCrewFallBackSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private RotateToFaceSystem _rotate = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WGCrewMedicSystem _medic = default!;
    [Dependency] private WGCrewSpeechSystem _speech = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;

    /// <summary>Total damage past which a crewman falls back, and under which he stops patching.</summary>
    public const float WoundedDamage = 50f;

    /// <summary>The least time between one fall back and the next.</summary>
    private static readonly TimeSpan FallBackGap = TimeSpan.FromSeconds(60);

    /// <summary>How long a crewman who found no cover waits before looking again.</summary>
    private static readonly TimeSpan RetryGap = TimeSpan.FromSeconds(5);

    /// <summary>How far he looks for cover, in tiles.</summary>
    private const int CoverRange = 5;

    /// <summary>The longest he patches before rejoining.</summary>
    private static readonly TimeSpan PatchLimit = TimeSpan.FromSeconds(8);

    /// <summary>The time between two treatments.</summary>
    private static readonly TimeSpan PatchGap = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether the crewman should fall back now, and to where: the nearest tile he can walk straight to that the
    /// hostile has no line of sight to. Failing to find one holds off the next look for a few seconds.
    /// </summary>
    public bool TryFindCover(EntityUid uid, out EntityCoordinates cover)
    {
        cover = default;
        var now = _timing.CurTime;
        if (!TryComp<WGCrewComponent>(uid, out var crew) || !WGCrewSkills.Of(crew).FallsBack
            || !TryComp<WGCrewWeaponComponent>(uid, out var weapon) || now < weapon.NextFallBack
            || HasComp<ActorComponent>(uid) || _weapons.IsStationCrew(uid) || !_mobs.IsAlive(uid) || !Wounded(uid)
            || Hostile(uid, weapon) is not { } hostile
            || Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp)
            || Transform(hostile).GridUid != grid)
            return false;

        var from = _xform.GetMapCoordinates(uid);
        var eyes = _xform.GetMapCoordinates(hostile);
        var centre = _maps.TileIndicesFor(grid, gridComp, from);
        var best = float.MaxValue;
        MapCoordinates? found = null;
        for (var dx = -CoverRange; dx <= CoverRange; dx++)
        {
            for (var dy = -CoverRange; dy <= CoverRange; dy++)
            {
                var tile = centre + new Vector2i(dx, dy);
                var spot = _maps.GridTileToWorld(grid, gridComp, tile);
                var distance = Vector2.Distance(spot.Position, from.Position);
                if (distance > CoverRange || distance >= best
                    || !_maps.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty
                    || _turf.IsTileBlocked(tileRef, CollisionGroup.Impassable)
                    || !_interaction.InRangeUnobstructed(from, spot, distance + 0.01f, CollisionGroup.Impassable,
                        e => e == uid || !Transform(e).Anchored)
                    || _interaction.InRangeUnobstructed(eyes, spot, Vector2.Distance(eyes.Position, spot.Position) + 0.01f,
                        CollisionGroup.Opaque, e => e == hostile || !Transform(e).Anchored))
                    continue;

                best = distance;
                found = spot;
            }
        }

        if (found is not { } at)
        {
            weapon.NextFallBack = now + RetryGap;
            return false;
        }

        cover = _xform.ToCoordinates(grid, at);
        return true;
    }

    /// <summary>Whether he is hurt past <see cref="WoundedDamage"/>.</summary>
    public bool Wounded(EntityUid uid)
    {
        return CompOrNull<DamageableComponent>(uid)?.TotalDamage > FixedPoint2.New(WoundedDamage);
    }

    /// <summary>The hostile he is breaking contact with: the one he is after or braced for, else any he would fight.</summary>
    private EntityUid? Hostile(EntityUid uid, WGCrewWeaponComponent weapon)
    {
        return _weapons.Focus((uid, weapon), CompOrNull<HTNComponent>(uid)) ?? _weapons.BraceTarget(uid)
            ?? _weapons.PickTarget(uid);
    }

    /// <summary>Starts a fall back: no other for a minute, and a call to his crew.</summary>
    public void Begin(EntityUid uid)
    {
        if (!TryComp<WGCrewWeaponComponent>(uid, out var weapon))
            return;

        weapon.NextFallBack = _timing.CurTime + FallBackGap;
        weapon.PatchUntil = TimeSpan.Zero;
        _speech.Say(uid, "fallback");
    }

    /// <summary>
    /// Patches the crewman up in cover, one treatment every <see cref="PatchGap"/>. False once he is under the
    /// threshold, has patched for <see cref="PatchLimit"/>, has nothing left to treat, or a hostile is in his sights.
    /// </summary>
    public bool Patch(EntityUid uid)
    {
        if (!TryComp<WGCrewWeaponComponent>(uid, out var weapon) || !_mobs.IsAlive(uid) || !Wounded(uid))
            return false;

        var now = _timing.CurTime;
        if (weapon.PatchUntil == TimeSpan.Zero)
        {
            weapon.PatchUntil = now + PatchLimit;
            weapon.NextPatch = now;
        }

        if (now >= weapon.PatchUntil)
            return false;

        if (_weapons.PickVisibleTarget(uid) != null)
            return false;

        if (now >= weapon.NextPatch)
        {
            weapon.NextPatch = now + PatchGap;
            if (!_medic.TreatSelf(uid))
                return false;
        }

        if (Hostile(uid, weapon) is { } hostile && !TerminatingOrDeleted(hostile))
            _rotate.TryFaceCoordinates(uid, _xform.GetWorldPosition(hostile));
        return true;
    }

    /// <summary>Ends a patch, so the next fall back starts its own.</summary>
    public void EndPatch(EntityUid uid)
    {
        if (TryComp<WGCrewWeaponComponent>(uid, out var weapon))
            weapon.PatchUntil = TimeSpan.Zero;
    }
}
