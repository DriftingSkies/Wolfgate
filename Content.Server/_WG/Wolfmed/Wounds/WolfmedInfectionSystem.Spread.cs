using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Performance;
using Content.Shared._WG.Wolfmed.CCVar;
using Content.Shared._WG.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Mobs.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WG.Wolfmed.Wounds;

/// <summary>Server cvars for spreading infection over ticks.</summary>
[CVarDefs]
public sealed class WolfmedInfectionSpreadCVars
{
    /// <summary>
    /// Seconds between turns for a corpse or a limb off any body, whose infection still runs but nobody is watching.
    /// A living body's turn comes every 5 s.
    /// </summary>
    public static readonly CVarDef<float> SlowSeconds =
        CVarDef.Create("wolfmed.infection_slow_seconds", 30f, CVar.SERVERONLY);
}

public sealed partial class WolfmedInfectionSystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// The infection work of one body, or of a part off any body: its infected wounds, infected and necrotic parts, and
    /// whether it is septic, as filed by the last <see cref="FileGroups"/>.
    /// </summary>
    private sealed class WGInfectionGroup
    {
        public EntityUid Key;
        public readonly List<Entity<WolfmedInfectionComponent, WoundComponent>> Wounds = new();
        public readonly List<EntityUid> InfectedParts = new();
        public readonly List<EntityUid> NecroticParts = new();
    }

    private readonly Dictionary<EntityUid, WGInfectionGroup> _groups = new();
    private readonly Stack<WGInfectionGroup> _spareGroups = new();
    private List<WGInfectionGroup>[] _slots = Array.Empty<List<WGInfectionGroup>>();
    private GameTick _nextFiling;

    /// <summary>When each group last had its turn, so a turn advances it by the time since.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _lastTurn = new();
    private readonly List<EntityUid> _staleTurns = new();

    /// <summary>When infection was turned off, while it is.</summary>
    private TimeSpan? _offSince;

    /// <summary>When infection last came back on, so a group first seen then isn't backdated into the time it was off.</summary>
    private TimeSpan _onSince;

    private readonly HashSet<EntityUid> _fevered = new();
    private readonly Dictionary<EntityUid, float> _feed = new();
    private readonly HashSet<EntityUid> _pinned = new();
    private readonly List<EntityUid> _spreading = new();
    private readonly List<EntityUid> _septic = new();

    /// <summary>
    /// Advances every infection on the server by <paramref name="seconds"/> at once, as the whole-server pass did. A test
    /// can hand it ten minutes in one call; ticks never come this way.
    /// </summary>
    public void AdvanceAll(float seconds)
    {
        FileGroups(1);
        var now = _timing.CurTime;
        if (!IsOn(now))
            return;

        var minutes = seconds / 60f * _config.GetCVar(WolfmedCVars.InfectionRate);
        var profile = Profile;
        var sepsis = _config.GetCVar(WolfmedCVars.SepsisEnabled);
        foreach (var group in _slots[0])
        {
            _lastTurn[group.Key] = now;
            TickGroup(group, profile, minutes, sepsis);
        }

        // The next tick files afresh, with the wounds this pass made.
        _nextFiling = GameTick.Zero;
    }

    /// <summary>
    /// One tick: every <see cref="TickSeconds"/> the work is filed by body, and each tick runs the groups whose turn it
    /// is (<see cref="WGTickSpread"/>), each advanced by the time since its own last turn. Corpses and loose limbs wait
    /// <see cref="WolfmedInfectionSpreadCVars.SlowSeconds"/> between turns.
    /// </summary>
    private void SpreadTick()
    {
        var period = WGTickSpread.Period(_timing, TickSeconds);
        var tick = _timing.CurTick;
        if (_slots.Length != period || tick >= _nextFiling)
        {
            FileGroups(period);
            _nextFiling = new GameTick(tick.Value + (uint) period);
        }

        var now = _timing.CurTime;
        if (!IsOn(now))
            return;

        var due = _slots[(int) (tick.Value % (uint) period)];
        if (due.Count == 0)
            return;

        var window = TimeSpan.FromSeconds(TickSeconds);
        var slow = TimeSpan.FromSeconds(Math.Max(TickSeconds, _config.GetCVar(WolfmedInfectionSpreadCVars.SlowSeconds)));
        var rate = _config.GetCVar(WolfmedCVars.InfectionRate);
        var sepsis = _config.GetCVar(WolfmedCVars.SepsisEnabled);
        var profile = Profile;

        // A group whose body has gone still has its turn: that is what takes the infection off its wounds.
        foreach (var group in due)
        {
            // A group seen for the first time counts as last turned a window ago, but no earlier than infection came on,
            // and keeps that, so a corpse's wait runs from when it was first filed.
            if (!_lastTurn.TryGetValue(group.Key, out var last) || last > now)
            {
                last = now - window;
                if (last < _onSince)
                    last = _onSince;
                _lastTurn[group.Key] = last;
            }

            var elapsed = now - last;
            if (IsSlow(group.Key) && elapsed < slow)
                continue;

            _lastTurn[group.Key] = now;
            TickGroup(group, profile, (float) elapsed.TotalSeconds / 60f * rate, sepsis);
        }
    }

    /// <summary>
    /// Whether infection is on. Once it comes back on, every last turn moves on by the time it was off, so that time
    /// isn't owed and the time before it still counts.
    /// </summary>
    private bool IsOn(TimeSpan now)
    {
        if (!_config.GetCVar(WolfmedCVars.InfectionEnabled))
        {
            _offSince ??= now;
            return false;
        }

        if (_offSince is not { } off)
            return true;

        _offSince = null;
        _onSince = now;
        var gap = now - off;
        _staleTurns.Clear();
        _staleTurns.AddRange(_lastTurn.Keys);
        foreach (var key in _staleTurns)
        {
            _lastTurn[key] += gap;
        }
        _staleTurns.Clear();
        return true;
    }

    /// <summary>A corpse, or a part off any body: nobody is watching its infection from moment to moment.</summary>
    private bool IsSlow(EntityUid key)
    {
        return !TryComp(key, out MobStateComponent? mob) || _mobState.IsDead(key, mob);
    }

    /// <summary>
    /// Files every infected wound, infected or necrotic part and septic body under its body, or under the part when it is
    /// off any body, and spreads the groups over <paramref name="period"/> slots. Walking the components is cheap; the
    /// work is in the turns.
    /// </summary>
    private void FileGroups(int period)
    {
        foreach (var group in _groups.Values)
        {
            group.Wounds.Clear();
            group.InfectedParts.Clear();
            group.NecroticParts.Clear();
            _spareGroups.Push(group);
        }

        _groups.Clear();
        if (_slots.Length != period)
        {
            _slots = new List<WGInfectionGroup>[period];
            for (var i = 0; i < period; i++)
            {
                _slots[i] = new List<WGInfectionGroup>();
            }
        }
        else
        {
            foreach (var slot in _slots)
            {
                slot.Clear();
            }
        }

        var wounds = EntityQueryEnumerator<WolfmedInfectionComponent, WoundComponent>();
        while (wounds.MoveNext(out var uid, out var infection, out var wound))
            Group(KeyOf(wound.HoldingPart), period).Wounds.Add((uid, infection, wound));

        var parts = EntityQueryEnumerator<WolfmedPartInfectionComponent>();
        while (parts.MoveNext(out var uid, out _))
            Group(KeyOf(uid), period).InfectedParts.Add(uid);

        var necrotic = EntityQueryEnumerator<WolfmedNecrosisComponent>();
        while (necrotic.MoveNext(out var uid, out var necrosis))
        {
            if (necrosis.Necrotic)
                Group(KeyOf(uid), period).NecroticParts.Add(uid);
        }

        var septic = EntityQueryEnumerator<WolfmedSepsisComponent>();
        while (septic.MoveNext(out var uid, out _))
            Group(uid, period);

        _staleTurns.Clear();
        foreach (var key in _lastTurn.Keys)
        {
            if (!_groups.ContainsKey(key))
                _staleTurns.Add(key);
        }

        foreach (var key in _staleTurns)
        {
            _lastTurn.Remove(key);
        }
    }

    /// <summary>The body a part is on, or the part itself when it is on none.</summary>
    private EntityUid KeyOf(EntityUid part) => CompOrNull<BodyPartComponent>(part)?.Body ?? part;

    private WGInfectionGroup Group(EntityUid key, int period)
    {
        if (_groups.TryGetValue(key, out var group))
            return group;

        group = _spareGroups.Count > 0 ? _spareGroups.Pop() : new WGInfectionGroup();
        group.Key = key;
        _groups[key] = group;
        _slots[(int) ((uint) key.Id % (uint) period)].Add(group);
        return group;
    }
}
