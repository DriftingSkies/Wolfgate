using System.Linq;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Drydock;
using Content.Server.Atmos.Components;
using Content.Server.Construction.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.Drydock;
using Content.Shared.Doors.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Colonies;

/// <summary>
/// The frame: a yard build stands in its berth as it is built. A design's deck and hull are read off its vessel once a
/// round; the frame, a grid of its own below the platform, has the deck laid from the keel outward over the first part
/// of the work and the hull raised over the rest, the platform's cranes swinging over each piece. A frame is round
/// state, built again up to its build's progress each round. When the build is done the frame makes way for the
/// finished vessel, in the same place.
/// </summary>
public sealed partial class WFColonyYardSystem
{
    [Dependency] private WFDrydockSystem _drydock = default!;

    /// <summary>The share of a build's work that lays its deck; the rest raises its hull.</summary>
    private const float DeckShare = 0.4f;

    /// <summary>Seconds of work past which a frame catches up without its cranes, as at a round's start.</summary>
    private const float CatchUp = 60f;

    private readonly Dictionary<string, FramePlan?> _plans = new();

    /// <summary>What a design's frame is made of, in the order it goes up.</summary>
    private sealed class FramePlan
    {
        public readonly List<(Vector2i Index, Tile Tile)> Deck = new();
        public readonly List<(EntProtoId Proto, Vector2 Position, Angle Rotation)> Hull = new();

        /// <summary>The finished vessel's bounds on the map, and where its grid sits and how it is turned, with the vessel loaded at the map's middle.</summary>
        public Box2 Bounds;
        public Vector2 Origin;
        public Angle Rotation;

        public int Count => Deck.Count + Hull.Count;
    }

    /// <summary>A design's frame plan, read off its vessel the first time it is asked for in a round.</summary>
    private FramePlan? PlanOf(WFYardDesignPrototype design)
    {
        if (_plans.TryGetValue(design.ID, out var known))
            return known;

        FramePlan? plan = null;
        // Loaded on a map of its own that never starts, so nothing aboard runs; only its deck and hull are read.
        var staging = _maps.CreateMap(out var stagingId, runMapInit: false);
        if (_proto.TryIndex(design.Vessel, out var vessel) && _mapLoader.TryLoadGrid(stagingId, vessel.ShuttlePath, out var loaded))
        {
            var grid = loaded.Value;
            plan = new FramePlan
            {
                Bounds = _transform.GetWorldMatrix(grid).TransformBox(grid.Comp.LocalAABB),
                Origin = _transform.GetWorldPosition(grid),
                Rotation = _transform.GetWorldRotation(grid),
            };
            var hull = grid.Comp.LocalAABB;
            var centre = hull.Center;
            var lengthwise = hull.Height >= hull.Width;
            // From the keel, the line down the vessel's length, outward.
            float FromKeel(Vector2 at) => lengthwise ? MathF.Abs(at.X - centre.X) : MathF.Abs(at.Y - centre.Y);

            var tiles = _maps.GetAllTilesEnumerator(grid, grid.Comp);
            while (tiles.MoveNext(out var tile))
            {
                plan.Deck.Add((tile.Value.GridIndices, tile.Value.Tile));
            }

            var children = Transform(grid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                var xform = Transform(child);
                if (!xform.Anchored || HasComp<DoorComponent>(child) || !HasComp<OccluderComponent>(child) && !HasComp<AirtightComponent>(child)
                    || MetaData(child).EntityPrototype is not { } proto)
                    continue;

                plan.Hull.Add((proto.ID, xform.LocalPosition, xform.LocalRotation));
            }

            plan.Deck.Sort((a, b) => Order(FromKeel((Vector2) a.Index + new Vector2(0.5f)), a.Index, FromKeel((Vector2) b.Index + new Vector2(0.5f)), b.Index));
            plan.Hull.Sort((a, b) => Order(FromKeel(a.Position), a.Position.Floored(), FromKeel(b.Position), b.Position.Floored()));
            if (plan.Deck.Count == 0)
                plan = null;
        }
        else
        {
            _log.Error($"The yard could not read the frame of {design.ID}.");
        }

        Del(staging);
        _plans[design.ID] = plan;
        return plan;
    }

    /// <summary>Nearest the keel first, then in a fixed order, so a frame goes up the same way every round.</summary>
    private static int Order(float keelA, Vector2i a, float keelB, Vector2i b)
    {
        var byKeel = keelA.CompareTo(keelB);
        if (byKeel != 0)
            return byKeel;

        var byY = a.Y.CompareTo(b.Y);
        return byY != 0 ? byY : a.X.CompareTo(b.X);
    }

    /// <summary>How many pieces of its plan a build's work has put up: the deck first, then the hull.</summary>
    private static int PiecesDue(FramePlan plan, JobState job, int stages)
    {
        var done = Math.Clamp((job.Stage + job.Progress) / Math.Max(1, stages), 0f, 1f);
        var deck = Math.Clamp(done / DeckShare, 0f, 1f) * plan.Deck.Count;
        var hull = Math.Clamp((done - DeckShare) / (1f - DeckShare), 0f, 1f) * plan.Hull.Count;
        // The keel is laid as the build is: a frame always has its first piece.
        return Math.Clamp((int) deck + (int) hull, 1, plan.Count);
    }

    /// <summary>
    /// Stands a build's frame below the platform if it has none, and puts up the pieces its work has earned since. The
    /// cranes swing over the newest piece when <paramref name="cranes"/> is set.
    /// </summary>
    private void TendFrame(JobState job, WFYardDesignPrototype design, EntityUid platform, bool cranes)
    {
        if (PlanOf(design) is not { } plan)
            return;

        if (job.Frame is not { } frame || TerminatingOrDeleted(frame))
        {
            frame = RaiseFrame(job, design, plan, platform);
            job.Frame = frame;
            job.Pieces = 0;
        }

        var grid = Comp<MapGridComponent>(frame);
        var due = PiecesDue(plan, job, design.Stages.Count);
        Vector2? newest = null;
        while (job.Pieces < due)
        {
            var index = job.Pieces++;
            if (index < plan.Deck.Count)
            {
                var (tile, kind) = plan.Deck[index];
                _maps.SetTile(frame, grid, tile, kind);
                newest = (Vector2) tile + new Vector2(0.5f);
                continue;
            }

            var (proto, position, rotation) = plan.Hull[index - plan.Deck.Count];
            var piece = Spawn(proto, new EntityCoordinates(frame, position));
            _transform.SetLocalRotation(piece, rotation);
            // A frame is the yard's work in hand, not salvage: its plating can't be taken down for the sheets.
            RemComp<ConstructionComponent>(piece);
            newest = position;
        }

        if (!cranes || newest is not { } at)
            return;

        var world = Vector2.Transform(at, _transform.GetWorldMatrix(frame));
        EntityUid? nearest = null;
        var best = float.MaxValue;
        foreach (var crane in CranesOf(platform))
        {
            var distance = MathF.Abs(_transform.GetWorldPosition(crane).X - world.X);
            if (distance >= best)
                continue;

            best = distance;
            nearest = crane;
        }

        if (nearest is { } working)
            _drydock.WorkCraneAt(working, frame, at);
    }

    /// <summary>Lays an empty frame for a build below the platform, clear of other grids and of the other berths' builds.</summary>
    private EntityUid RaiseFrame(JobState job, WFYardDesignPrototype design, FramePlan plan, EntityUid platform)
    {
        var map = Transform(platform).MapID;
        var platformBox = _transform.GetWorldMatrix(platform).TransformBox(Comp<MapGridComponent>(platform).LocalAABB);
        var grids = new List<Entity<MapGridComponent>>();
        var top = platformBox.Bottom - HullGap;
        var position = Vector2.Zero;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            position = new Vector2(platformBox.Center.X - plan.Bounds.Center.X, top - plan.Bounds.Top);
            var box = plan.Bounds.Translated(position).Enlarged(1f);
            var below = float.MaxValue;
            grids.Clear();
            _maps.FindGridsIntersecting(map, box, ref grids, approx: true, includeMap: false);
            foreach (var grid in grids)
            {
                below = MathF.Min(below, _transform.GetWorldMatrix(grid.Owner).TransformBox(grid.Comp.LocalAABB).Bottom);
            }

            // A frame just begun is a few tiles: keep clear of where the other builds will stand when done.
            foreach (var other in _jobs)
            {
                if (other != job && other.FrameBox is { } taken && other.Frame is { } standing && !TerminatingOrDeleted(standing)
                    && Transform(standing).MapID == map && taken.Intersects(box))
                    below = MathF.Min(below, taken.Bottom);
            }

            if (below == float.MaxValue)
                break;

            top = below - HullGap;
        }

        var frame = _maps.CreateGridEntity(map);
        _transform.SetWorldPosition(frame, position + plan.Origin);
        _transform.SetWorldRotation(frame, plan.Rotation);
        job.FrameAt = position;
        var name = _proto.TryIndex(design.Vessel, out var vessel) ? vessel.Name : design.ID;
        _meta.SetEntityName(frame, Loc.GetString("wf-yard-frame-name", ("design", name)));
        _maps.SetTile(frame, frame.Comp, plan.Deck[0].Index, plan.Deck[0].Tile);
        frame.Comp.CanSplit = false;
        _physics.SetBodyType(frame, BodyType.Static);
        EnsureComp<CleanupImmuneComponent>(frame);
        EnsureComp<WFColonyYardFrameComponent>(frame).World = job.World;
        job.FrameBox = plan.Bounds.Translated(position);
        return frame;
    }

    /// <summary>
    /// Takes a build's frame away, leaving whoever and whatever was loose on it where it stood. Returns where to load
    /// the finished vessel for it to stand in the frame's place.
    /// </summary>
    private Vector2? ClearFrame(JobState job)
    {
        var frame = job.Frame;
        var position = job.FrameAt;
        job.Frame = null;
        job.FrameBox = null;
        job.Pieces = 0;
        if (frame is not { } uid || TerminatingOrDeleted(uid))
            return null;

        if (Transform(uid).MapUid is { } map)
        {
            var loose = new List<EntityUid>();
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!Transform(child).Anchored)
                    loose.Add(child);
            }

            foreach (var child in loose)
            {
                _transform.SetCoordinates(child, new EntityCoordinates(map, _transform.GetWorldPosition(child)));
            }
        }

        Del(uid);
        return position;
    }

    /// <summary>The cranes standing on a platform.</summary>
    private List<EntityUid> CranesOf(EntityUid platform)
    {
        var cranes = new List<EntityUid>();
        var children = Transform(platform).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (HasComp<WFDrydockCraneComponent>(child))
                cranes.Add(child);
        }

        return cranes;
    }

    /// <summary>Folds a platform's cranes away once no build on it is being worked.</summary>
    private void ParkCranes(EntityUid platform)
    {
        foreach (var crane in CranesOf(platform))
        {
            _drydock.ParkCrane(crane);
        }
    }

    /// <summary>Forgets every frame and plan, as a new round or a reread of the yards does; frames still standing go.</summary>
    private void ForgetFrames()
    {
        _plans.Clear();
        var frames = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFColonyYardFrameComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            frames.Add(uid);
        }

        foreach (var uid in frames)
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        foreach (var job in _jobs)
        {
            job.Frame = null;
            job.FrameBox = null;
            job.Pieces = 0;
        }
    }

    /// <summary>A build's frame this round, if it stands: for tests.</summary>
    public EntityUid? FrameOf(string world, string design)
    {
        return _jobs.FirstOrDefault(job => job.World == world && job.Design == design)?.Frame;
    }
}
