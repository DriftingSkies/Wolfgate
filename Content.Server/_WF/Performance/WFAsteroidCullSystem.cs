using System.Numerics;
using Content.Server._WF.AsteroidMining;
using Content.Server.Decals;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._WF.AsteroidMining;
using Content.Shared._WF.Performance;
using Content.Shared.GameTicking;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Performance;

/// <summary>
/// Sends mining rock grids only to players whose view has come near them, instead of to everyone as the engine does
/// for grids, and keeps a <see cref="WFAsteroidFieldComponent"/> per worldgen chunk so every other client's radar and
/// far overlay still draw the rocks. Rocks no chunk owns stay sent to everyone.
/// </summary>
/// <remarks>
/// A player keeps a rock grid once sent, until it is deleted: the client can't detach a grid that leaves its view
/// (the engine asserts against it), so sending stops only when the rock goes.
/// </remarks>
public sealed class WFAsteroidCullSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private DebrisFeaturePlacerSystem _debris = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan PassInterval = TimeSpan.FromSeconds(0.5);
    private const float MoveThreshold = 0.5f;
    private const float TurnThreshold = 0.05f;
    /// <summary>Passes a rock grid may go unowned by a chunk before it goes back to being sent to everyone.</summary>
    private const int OwnerPasses = 2;

    private bool _enabled;
    private float _range;
    private TimeSpan _nextPass;

    /// <summary>Culled rock grids no field holds yet, and the passes they have waited.</summary>
    private readonly Dictionary<EntityUid, int> _unassigned = new();
    private readonly Dictionary<EntityUid, EntityUid> _fieldOf = new();
    private readonly Dictionary<EntityUid, EntityUid> _fieldByChunk = new();
    private readonly Dictionary<EntityUid, EntityUid> _chunkOfField = new();
    private readonly Dictionary<EntityUid, Dictionary<EntityUid, FieldRock>> _fieldRocks = new();
    private readonly HashSet<EntityUid> _dirtyFields = new();
    /// <summary>Rock grids created since the last tick, sent to nearby players at once so new fragments don't pop in late.</summary>
    private readonly List<EntityUid> _fresh = new();
    private readonly Dictionary<ICommonSession, HashSet<EntityUid>> _sent = new();
    private readonly List<EntityUid> _scratch = new();
    private List<Entity<MapGridComponent>> _gridHits = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridInitializeEvent>(OnGridInit, after: new[] { typeof(PvsOverrideSystem), typeof(DecalSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());
        _players.PlayerStatusChanged += OnPlayerStatus;
        _cfg.OnValueChanged(WFPerformanceCVars.AsteroidSendRange, r => _range = r, true);
        _cfg.OnValueChanged(WFPerformanceCVars.AsteroidCulling, OnEnabledChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatus;
    }

    /// <summary>Whether a grid is a rock grid only sent to players near it.</summary>
    public bool IsCulled(EntityUid grid)
    {
        return _fieldOf.ContainsKey(grid) || _unassigned.ContainsKey(grid);
    }

    private void OnGridInit(GridInitializeEvent ev)
    {
        if (!_enabled || !HasComp<WFAsteroidGridComponent>(ev.EntityUid))
            return;

        _pvs.RemoveForceSend(ev.EntityUid);
        _unassigned[ev.EntityUid] = 0;
        _fresh.Add(ev.EntityUid);
    }

    private void OnEnabledChanged(bool enabled)
    {
        if (_enabled && !enabled)
        {
            // Back to the engine's way: every rock grid to everyone, and no fields.
            foreach (var grid in _fieldOf.Keys)
            {
                if (!TerminatingOrDeleted(grid))
                    _pvs.AddForceSend(grid);
            }

            foreach (var grid in _unassigned.Keys)
            {
                if (!TerminatingOrDeleted(grid))
                    _pvs.AddForceSend(grid);
            }

            foreach (var field in _chunkOfField.Keys)
            {
                QueueDel(field);
            }

            Clear();
        }

        _enabled = enabled;
    }

    private void OnPlayerStatus(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            _sent.Remove(args.Session);
    }

    private void Clear()
    {
        _unassigned.Clear();
        _fieldOf.Clear();
        _fieldByChunk.Clear();
        _chunkOfField.Clear();
        _fieldRocks.Clear();
        _dirtyFields.Clear();
        _fresh.Clear();
        _sent.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_enabled)
            return;

        if (_fresh.Count > 0)
            SendFresh();

        if (_timing.CurTime < _nextPass)
            return;

        _nextPass = _timing.CurTime + PassInterval;
        AssignNew();
        RefreshMembers();
        FlushFields();
        SyncSessions();
    }

    /// <summary>Puts new rock grids into their chunk's field, or sends them to everyone if no chunk owns them.</summary>
    private void AssignNew()
    {
        _scratch.Clear();
        _scratch.AddRange(_unassigned.Keys);
        foreach (var grid in _scratch)
        {
            if (TerminatingOrDeleted(grid))
            {
                _unassigned.Remove(grid);
                continue;
            }

            if (_debris.GetDebrisOwner(grid) is { } chunk && Transform(grid).MapUid is { } map)
            {
                _unassigned.Remove(grid);
                Join(grid, GetField(chunk, map));
                continue;
            }

            if (++_unassigned[grid] > OwnerPasses)
            {
                _unassigned.Remove(grid);
                _pvs.AddForceSend(grid);
            }
        }
    }

    /// <summary>Drops deleted rocks, moves rocks that changed chunk, and updates rocks that moved or changed look.</summary>
    private void RefreshMembers()
    {
        _scratch.Clear();
        _scratch.AddRange(_fieldOf.Keys);
        foreach (var grid in _scratch)
        {
            var field = _fieldOf[grid];
            if (TerminatingOrDeleted(grid) || TerminatingOrDeleted(field))
            {
                Leave(grid);
                continue;
            }

            if (_debris.GetDebrisOwner(grid) is { } chunk && _chunkOfField.TryGetValue(field, out var fieldChunk) &&
                chunk != fieldChunk && Transform(grid).MapUid is { } map)
            {
                Leave(grid);
                Join(grid, GetField(chunk, map));
                continue;
            }

            Store(grid, field);
        }
    }

    private void Join(EntityUid grid, EntityUid field)
    {
        _fieldOf[grid] = field;
        Store(grid, field);
    }

    private void Leave(EntityUid grid)
    {
        if (!_fieldOf.Remove(grid, out var field))
            return;

        if (_fieldRocks.TryGetValue(field, out var rocks) && rocks.Remove(grid))
            _dirtyFields.Add(field);

        foreach (var sent in _sent.Values)
        {
            sent.Remove(grid);
        }
    }

    /// <summary>Stores a rock's current place and look in its field if it has changed enough to matter.</summary>
    private void Store(EntityUid grid, EntityUid field)
    {
        if (!TryComp<WFAsteroidGridVisualsComponent>(grid, out var visuals) || visuals.Rock is not { } rockProto)
            return;

        var (position, rotation) = _transform.GetWorldPositionRotation(grid);
        var rock = new FieldRock(
            position + rotation.RotateVec(new Vector2(0.5f, 0.5f)),
            (float) rotation.Theta,
            rockProto,
            visuals.Variant,
            visuals.Look,
            visuals.RockColor,
            visuals.VeinColor);

        var rocks = _fieldRocks.GetOrNew(field);
        if (rocks.TryGetValue(grid, out var old) && old.Close(rock))
            return;

        rocks[grid] = rock;
        _dirtyFields.Add(field);
    }

    private EntityUid GetField(EntityUid chunk, EntityUid map)
    {
        if (_fieldByChunk.TryGetValue(chunk, out var existing) && !TerminatingOrDeleted(existing))
            return existing;

        var field = Spawn(null, new EntityCoordinates(map, Vector2.Zero));
        _metaData.SetEntityName(field, Loc.GetString("wf-asteroid-field-name"));
        AddComp<WFAsteroidFieldComponent>(field);
        _pvs.AddForceSend(field);
        _fieldByChunk[chunk] = field;
        _chunkOfField[field] = chunk;
        return field;
    }

    /// <summary>Rebuilds the networked list of every field that changed, and deletes fields left empty.</summary>
    private void FlushFields()
    {
        foreach (var field in _dirtyFields)
        {
            if (TerminatingOrDeleted(field) || !TryComp<WFAsteroidFieldComponent>(field, out var comp))
                continue;

            if (!_fieldRocks.TryGetValue(field, out var rocks) || rocks.Count == 0)
            {
                _fieldRocks.Remove(field);
                if (_chunkOfField.Remove(field, out var chunk))
                    _fieldByChunk.Remove(chunk);
                QueueDel(field);
                continue;
            }

            comp.Protos.Clear();
            comp.Looks.Clear();
            comp.Rocks.Clear();
            foreach (var (grid, rock) in rocks)
            {
                comp.Rocks.Add(new WFAsteroidFieldRock(
                    GetNetEntity(grid),
                    rock.Position,
                    rock.Rotation,
                    Index(comp.Protos, rock.Proto.Id),
                    (byte) Math.Clamp(rock.Variant, 0, byte.MaxValue),
                    rock.Look is { } look ? Index(comp.Looks, look) : WFAsteroidFieldRock.NoLook,
                    rock.RockColor,
                    rock.VeinColor));
            }

            Dirty(field, comp);
        }

        _dirtyFields.Clear();
    }

    private static byte Index(List<string> table, string value)
    {
        var index = table.IndexOf(value);
        if (index < 0)
        {
            index = table.Count;
            table.Add(value);
        }

        return (byte) Math.Min(index, byte.MaxValue - 1);
    }

    /// <summary>Sends each player the rock grids that have come near any of their views.</summary>
    private void SyncSessions()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
                continue;

            var sent = _sent.GetOrNew(session);
            if (session.AttachedEntity is { } attached)
                SendNear(session, attached, sent);

            foreach (var viewer in session.ViewSubscriptions)
            {
                SendNear(session, viewer, sent);
            }
        }
    }

    private void SendFresh()
    {
        foreach (var session in _players.Sessions)
        {
            if (session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
                continue;

            var sent = _sent.GetOrNew(session);
            if (session.AttachedEntity is { } attached)
                SendFreshNear(session, attached, sent);

            foreach (var viewer in session.ViewSubscriptions)
            {
                SendFreshNear(session, viewer, sent);
            }
        }

        _fresh.Clear();
    }

    private void SendFreshNear(ICommonSession session, EntityUid viewer, HashSet<EntityUid> sent)
    {
        if (TerminatingOrDeleted(viewer))
            return;

        var coordinates = _transform.GetMapCoordinates(viewer);
        foreach (var grid in _fresh)
        {
            if (TerminatingOrDeleted(grid) || sent.Contains(grid))
                continue;

            var gridCoordinates = _transform.GetMapCoordinates(grid);
            if (gridCoordinates.MapId != coordinates.MapId ||
                (gridCoordinates.Position - coordinates.Position).Length() >= _range)
                continue;

            _pvs.AddForceSend(grid, session);
            sent.Add(grid);
        }
    }

    private void SendNear(ICommonSession session, EntityUid viewer, HashSet<EntityUid> sent)
    {
        if (TerminatingOrDeleted(viewer))
            return;

        var coordinates = _transform.GetMapCoordinates(viewer);
        if (coordinates.MapId == MapId.Nullspace)
            return;

        _gridHits.Clear();
        _map.FindGridsIntersecting(coordinates.MapId, Box2.CenteredAround(coordinates.Position, new Vector2(_range * 2f)),
            ref _gridHits);
        foreach (var (grid, _) in _gridHits)
        {
            if (!IsCulled(grid) || sent.Contains(grid) ||
                (_transform.GetWorldPosition(grid) - coordinates.Position).Length() >= _range)
                continue;

            _pvs.AddForceSend(grid, session);
            sent.Add(grid);
        }
    }

    private readonly record struct FieldRock(
        Vector2 Position,
        float Rotation,
        EntProtoId Proto,
        int Variant,
        string? Look,
        Color RockColor,
        Color VeinColor)
    {
        public bool Close(FieldRock other)
        {
            return Proto == other.Proto && Variant == other.Variant && Look == other.Look &&
                   RockColor == other.RockColor && VeinColor == other.VeinColor &&
                   (Position - other.Position).LengthSquared() < MoveThreshold * MoveThreshold &&
                   MathF.Abs(Rotation - other.Rotation) < TurnThreshold;
        }
    }
}
