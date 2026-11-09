using System.Linq;
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Lattice;
using Content.Shared.Camera;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The Coda's leviathan. It can't be fought and it doesn't hunt: now and then it finds a crewed ship, circles at the
/// edge of its radar, slides past beneath it and sometimes brushes the hull before sinking away. It is there to be
/// felt, not to kill: the worst it does is shove the ship, cut its engines a moment and tear off a few outer tiles.
/// </summary>
public sealed partial class WFCodaHazardSystem
{
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private WFDriftMindSystem _mind = default!;

    private static readonly SoundSpecifier LeviathanCall = new SoundCollectionSpecifier("WFDreadMenace",
        AudioParams.Default.WithPitchScale(0.35f).WithVolume(4f));

    private static readonly TimeSpan PassAfter = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan TouchAfter = TimeSpan.FromSeconds(32);
    private static readonly TimeSpan LeaveAfter = TimeSpan.FromSeconds(45);

    /// <summary>How long it takes to slide past beneath the ship.</summary>
    private const float PassSeconds = 12f;

    /// <summary>Chance its pass ends in a touch.</summary>
    private const float TouchChance = 0.5f;

    /// <summary>Drift it leaves in the head of everyone aboard.</summary>
    private const float LeviathanDrift = 8f;

    private sealed class Leviathan
    {
        public EntityUid Grid;
        public TimeSpan Started;
        public Vector2 Direction;
        public int Seed;
        public bool Touches;
        public int Tear;
        public int Stage;
    }

    private readonly List<Leviathan> _leviathans = new();

    /// <summary>Rolls whether the leviathan finds a ship this tick.</summary>
    private void RollLeviathan(Entity<MapGridComponent> grid, Ship ship, List<EntityUid> crew, float depth,
        WFLatticeProfilePrototype profile, float seconds, TimeSpan now)
    {
        if (now < ship.LeviathanAfter || _leviathans.Exists(l => l.Grid == grid.Owner)
            || !_random.Prob(profile.LeviathanChance * (1f + depth) * seconds / 60f))
            return;

        ship.LeviathanAfter = now + TimeSpan.FromSeconds(profile.LeviathanCooldown);
        StartLeviathan(grid.Owner, profile);
    }

    /// <summary>Sends the leviathan to a ship now; whether it touches the hull is rolled unless given.</summary>
    public void StartLeviathan(EntityUid grid, WFLatticeProfilePrototype profile, bool? touch = null)
    {
        var leviathan = new Leviathan
        {
            Grid = grid,
            Started = _timing.CurTime,
            Direction = _random.NextAngle().ToVec(),
            Seed = _random.Next(),
            Touches = touch ?? _random.Prob(TouchChance),
            Tear = profile.LeviathanTear,
        };

        _leviathans.Add(leviathan);
        Tell(leviathan, WFLeviathanPhase.Approach, (float) PassAfter.TotalSeconds);
        Announce(grid, "wf-coda-leviathan-approach", LeviathanCall, PopupType.MediumCaution);
    }

    private void UpdateLeviathans(TimeSpan now)
    {
        for (var i = _leviathans.Count - 1; i >= 0; i--)
        {
            var leviathan = _leviathans[i];
            if (TerminatingOrDeleted(leviathan.Grid))
            {
                _leviathans.RemoveAt(i);
                continue;
            }

            var since = now - leviathan.Started;
            switch (leviathan.Stage)
            {
                case 0 when since >= PassAfter:
                    leviathan.Stage = 1;
                    Pass(leviathan, now);
                    break;
                case 1 when since >= TouchAfter:
                    leviathan.Stage = 2;
                    if (leviathan.Touches)
                        Touch(leviathan, now);
                    break;
                case 2 when since >= LeaveAfter:
                    Leave(leviathan);
                    _leviathans.RemoveAt(i);
                    break;
            }
        }
    }

    /// <summary>It slides past beneath the ship: the lights go, the hull groans, everyone's footing goes.</summary>
    private void Pass(Leviathan leviathan, TimeSpan now)
    {
        Tell(leviathan, WFLeviathanPhase.Pass, PassSeconds);
        FlutterPower(leviathan.Grid, now + TimeSpan.FromSeconds(8));
        Announce(leviathan.Grid, "wf-coda-leviathan-pass", GroanSound, PopupType.LargeCaution);
        foreach (var mob in Crew(leviathan.Grid))
        {
            _recoil.KickCamera(mob, _random.NextAngle().ToVec() * 1.5f);
        }
    }

    /// <summary>It brushes the hull: a shove, the engines cut a moment, and a few outer tiles torn away.</summary>
    private void Touch(Leviathan leviathan, TimeSpan now)
    {
        if (!TryComp<MapGridComponent>(leviathan.Grid, out var grid))
            return;

        if (TryComp<PhysicsComponent>(leviathan.Grid, out var body))
            _physics.ApplyLinearImpulse(leviathan.Grid, leviathan.Direction * body.Mass * 4f, body: body);

        FailThrusters(leviathan.Grid, ThrusterType.Linear, now + TimeSpan.FromSeconds(6));
        Tear((leviathan.Grid, grid), leviathan.Tear);
        Announce(leviathan.Grid, "wf-coda-leviathan-touch", GroanSound, PopupType.LargeCaution);
        foreach (var mob in Crew(leviathan.Grid))
        {
            _recoil.KickCamera(mob, leviathan.Direction * 3f);
        }
    }

    /// <summary>It sinks away, and leaves something of itself in everyone who felt it.</summary>
    private void Leave(Leviathan leviathan)
    {
        foreach (var mob in Crew(leviathan.Grid))
        {
            _mind.Add(mob, LeviathanDrift, WFDriftFlavour.Unmooring);
            _popup.PopupEntity(Loc.GetString("wf-coda-leviathan-leave"), mob, mob, PopupType.Medium);
        }
    }

    /// <summary>
    /// Tears up to a few outer tiles off the hull: dead-end tips first, then convex corners whose neighbours stay joined,
    /// so the ship never splits. Never under anyone, nor under a helm.
    /// </summary>
    private void Tear(Entity<MapGridComponent> grid, int most)
    {
        var tips = new List<Vector2i>();
        var corners = new List<Vector2i>();
        foreach (var tile in _maps.GetAllTiles(grid.Owner, grid.Comp))
        {
            var at = tile.GridIndices;
            var up = Filled(grid, at + new Vector2i(0, 1));
            var down = Filled(grid, at + new Vector2i(0, -1));
            var left = Filled(grid, at + new Vector2i(-1, 0));
            var right = Filled(grid, at + new Vector2i(1, 0));
            var filled = (up ? 1 : 0) + (down ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);
            if (filled == 0 || !Clear(grid, at))
                continue;

            if (filled == 1)
            {
                tips.Add(at);
                continue;
            }

            // Two filled neighbours at a right angle, joined by the tile between them, so taking this one splits nothing.
            if (filled == 2 && up != down)
            {
                var diagonal = at + new Vector2i(right ? 1 : -1, up ? 1 : -1);
                if (Filled(grid, diagonal))
                    corners.Add(at);
            }
        }

        var torn = 0;
        foreach (var at in tips.OrderBy(_ => _random.Next()).Concat(corners.OrderBy(_ => _random.Next())))
        {
            if (torn >= most)
                break;

            // An earlier tear may have changed this tile's neighbours, so check it is still safe to take.
            if (!Clear(grid, at) || !Removable(grid, at))
                continue;

            _maps.SetTile(grid.Owner, grid.Comp, at, Tile.Empty);
            torn++;
        }
    }

    /// <summary>Whether taking a tile leaves the rest joined: a dead-end tip, or a corner whose two neighbours meet diagonally.</summary>
    private bool Removable(Entity<MapGridComponent> grid, Vector2i at)
    {
        var up = Filled(grid, at + new Vector2i(0, 1));
        var down = Filled(grid, at + new Vector2i(0, -1));
        var left = Filled(grid, at + new Vector2i(-1, 0));
        var right = Filled(grid, at + new Vector2i(1, 0));
        var filled = (up ? 1 : 0) + (down ? 1 : 0) + (left ? 1 : 0) + (right ? 1 : 0);
        if (filled == 1)
            return true;

        return filled == 2 && up != down && Filled(grid, at + new Vector2i(right ? 1 : -1, up ? 1 : -1));
    }

    private bool Filled(Entity<MapGridComponent> grid, Vector2i at)
    {
        return _maps.TryGetTileRef(grid.Owner, grid.Comp, at, out var tile) && !tile.Tile.IsEmpty;
    }

    /// <summary>Whether a tile has no one on it or beside it, no helm, and no flesh knot.</summary>
    private bool Clear(Entity<MapGridComponent> grid, Vector2i at)
    {
        if (!Filled(grid, at))
            return false;

        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, at);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<ShuttleConsoleComponent>(uid) || HasComp<WFCodaFleshNodeComponent>(uid))
                return false;
        }

        var centre = _maps.GridTileToLocal(grid.Owner, grid.Comp, at);
        foreach (var mob in Crew(grid.Owner))
        {
            if (_transform.InRange(Transform(mob).Coordinates, centre, 2f))
                return false;
        }

        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (mobs.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid.Owner && _transform.InRange(xform.Coordinates, centre, 1.5f))
                return false;
        }

        return true;
    }

    private List<EntityUid> Crew(EntityUid grid)
    {
        return _crew.TryGetValue(grid, out var list) ? list : new List<EntityUid>();
    }

    private void Tell(Leviathan leviathan, WFLeviathanPhase phase, float seconds)
    {
        var ev = new WFLeviathanEvent(GetNetEntity(leviathan.Grid), phase, seconds, leviathan.Seed, leviathan.Direction);
        foreach (var mob in Crew(leviathan.Grid))
        {
            if (TryComp<ActorComponent>(mob, out var actor))
                RaiseNetworkEvent(ev, actor.PlayerSession);
        }
    }

    private void Announce(EntityUid grid, string message, SoundSpecifier sound, PopupType type)
    {
        foreach (var mob in Crew(grid))
        {
            _popup.PopupEntity(Loc.GetString(message), mob, mob, type);
            if (TryComp<ActorComponent>(mob, out var actor))
                _audio.PlayGlobal(sound, actor.PlayerSession);
        }
    }
}
