using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WG.NetTraffic;

/// <summary>
/// On the server, sends an anchored structure's damage at most once an interval: a wall under autocannon fire changes
/// every hit, and each change went to every player in view. Mobs, body parts, items and anything else not anchored, and
/// anything on the client, are sent as before.
/// </summary>
public sealed class WGDamageNetSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    private TimeSpan _interval;
    private readonly Dictionary<EntityUid, TimeSpan> _lastSent = new();
    private readonly Dictionary<EntityUid, TimeSpan> _pending = new();
    private readonly List<EntityUid> _due = new();

    public override void Initialize()
    {
        base.Initialize();
        if (_net.IsServer)
            Subs.CVar(_config, NetTrafficCVars.StructureDamageInterval, value => _interval = TimeSpan.FromSeconds(value), true);
        SubscribeLocalEvent<DamageableComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<DamageableComponent> ent, ref ComponentShutdown args)
    {
        _lastSent.Remove(ent);
        _pending.Remove(ent);
    }

    /// <summary>
    /// Whether a damage change on this entity waits to be sent. True means the caller skips dirtying it now; it is
    /// dirtied once the interval since it was last sent is up.
    /// </summary>
    public bool Defer(EntityUid uid)
    {
        if (_net.IsClient || _interval <= TimeSpan.Zero || HasComp<MobStateComponent>(uid)
            || !TryComp(uid, out TransformComponent? xform) || !xform.Anchored)
            return false;

        var now = _timing.CurTime;
        if (_pending.ContainsKey(uid))
            return true;

        if (_lastSent.TryGetValue(uid, out var last) && now - last < _interval)
        {
            _pending[uid] = last + _interval;
            return true;
        }

        _lastSent[uid] = now;
        return false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_pending.Count == 0)
            return;

        var now = _timing.CurTime;
        foreach (var (uid, due) in _pending)
        {
            if (now >= due)
                _due.Add(uid);
        }

        foreach (var uid in _due)
        {
            _pending.Remove(uid);
            if (!TryComp<DamageableComponent>(uid, out var damageable))
                continue;

            _lastSent[uid] = now;
            Dirty(uid, damageable);
            if (TryComp<AppearanceComponent>(uid, out var appearance))
            {
                var data = new DamageVisualizerGroupData(damageable.DamagePerGroup.Keys.ToList());
                _appearance.SetData(uid, DamageVisualizerKeys.DamageUpdateGroups, data, appearance);
            }
        }

        _due.Clear();
    }
}
