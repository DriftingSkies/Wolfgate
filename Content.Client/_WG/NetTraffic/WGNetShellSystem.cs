using System.Numerics;
using Content.Shared._WG.NetTraffic;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Client.Timing;
using Robust.Shared.Timing;

namespace Content.Client._WG.NetTraffic;

/// <summary>Draws the ship shells the server keeps off the network: a local sprite flown straight from where it was fired.</summary>
public sealed class WGNetShellSystem : EntitySystem
{
    [Dependency] private IClientGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The components a shell's copy takes from its prototype; the rest is the server's business.</summary>
    private static readonly string[] Visuals = { "Sprite", "PointLight" };

    private readonly Dictionary<int, Shell> _shells = new();

    /// <summary>How many shells are being drawn.</summary>
    public int Count => _shells.Count;
    private readonly Dictionary<string, ComponentRegistry> _registries = new();
    private readonly List<int> _expired = new();

    private sealed class Shell
    {
        public EntityUid Entity;
        public Vector2 Position;
        public Vector2 Velocity;
        public TimeSpan From;
        public TimeSpan Until;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGShellBatchEvent>(OnBatch);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Clear();
    }

    private void OnBatch(WGShellBatchEvent ev)
    {
        foreach (var fired in ev.Fired)
        {
            if (!TryGetEntity(fired.Map, out var map) || !TryComp<MapComponent>(map, out var mapComp))
                continue;

            var entity = Spawn(null, new MapCoordinates(fired.Position, mapComp.MapId));
            if (Registry(fired.Prototype) is { } registry)
                EntityManager.AddComponents(entity, registry);
            _transform.SetWorldRotation(entity, new Angle(fired.Rotation));
            var from = Since(fired.Tick);
            if (_shells.Remove(fired.Id, out var old))
                Discard(old.Entity);
            _shells[fired.Id] = new Shell
            {
                Entity = entity,
                Position = fired.Position,
                Velocity = fired.Velocity,
                From = from,
                Until = from + TimeSpan.FromSeconds(fired.Lifetime + 1f),
            };
        }

        foreach (var moved in ev.Moved)
        {
            if (!_shells.TryGetValue(moved.Id, out var shell))
                continue;

            shell.Position = moved.Position;
            shell.Velocity = moved.Velocity;
            shell.From = Since(moved.Tick);
            if (!TerminatingOrDeleted(shell.Entity))
                _transform.SetWorldRotation(shell.Entity, new Angle(moved.Rotation));
        }

        foreach (var ended in ev.Ended)
        {
            if (_shells.Remove(ended.Id, out var shell))
                Discard(shell.Entity);
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.RealTime;
        foreach (var (id, shell) in _shells)
        {
            if (now > shell.Until || TerminatingOrDeleted(shell.Entity) || Transform(shell.Entity).MapUid == null)
            {
                _expired.Add(id);
                continue;
            }

            var age = (float) (now - shell.From).TotalSeconds;
            _transform.SetWorldPosition(shell.Entity, shell.Position + shell.Velocity * age);
        }

        foreach (var id in _expired)
        {
            if (_shells.Remove(id, out var shell))
                Discard(shell.Entity);
        }

        _expired.Clear();
    }

    /// <summary>The local time a server tick happened, judged from the last tick the client has applied.</summary>
    private TimeSpan Since(uint tick)
    {
        var behind = Math.Max(0L, (long) _timing.LastRealTick.Value - tick);
        return _timing.RealTime - _timing.TickPeriod * behind;
    }

    private ComponentRegistry? Registry(string id)
    {
        if (_registries.TryGetValue(id, out var cached))
            return cached;

        if (!_prototypes.TryIndex(id, out var proto))
            return null;

        var registry = new ComponentRegistry();
        foreach (var name in Visuals)
        {
            if (proto.Components.TryGetValue(name, out var entry))
                registry[name] = entry;
        }

        _registries[id] = registry;
        return registry;
    }

    /// <summary>Deletes a copy unless it already went, such as with its map.</summary>
    private void Discard(EntityUid entity)
    {
        if (!TerminatingOrDeleted(entity))
            QueueDel(entity);
    }

    private void Clear()
    {
        foreach (var shell in _shells.Values)
        {
            Discard(shell.Entity);
        }

        _shells.Clear();
    }
}
