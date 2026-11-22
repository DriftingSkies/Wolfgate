using System.Reflection;
using Content.Server._WG.Dormancy;
using Prometheus;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Server._WG.Performance;

/// <summary>Server cvars for <see cref="WGNetDispatchWatchSystem"/>.</summary>
[CVarDefs]
public sealed class WGNetDispatchCVars
{
    /// <summary>Whether client messages are timed by type.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.net.dispatch_watch", true, CVar.SERVERONLY);

    /// <summary>A client message whose handlers take at least this many milliseconds is logged; 0 turns it off.</summary>
    public static readonly CVarDef<float> SlowMessageMs =
        CVarDef.Create("wg.net.slow_message_ms", 25f, CVar.SERVERONLY);
}

/// <summary>
/// Times the client messages the server handles in its EntityNet stage, by message type: a BUI message by the message
/// inside it, an input by its key function. Exports <c>wg_net_dispatch_seconds{type}</c> and logs slow messages with
/// their sender.
/// </summary>
public sealed class WGNetDispatchWatchSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;

    private static readonly Histogram DispatchSeconds = Metrics.CreateHistogram(
        "wg_net_dispatch_seconds",
        "Time the server spent handling one client message, by message type",
        new HistogramConfiguration
        {
            LabelNames = ["type"],
            Buckets = Histogram.ExponentialBuckets(0.0001, 2, 14),
        });

    private static readonly Histogram? EngineUsage = typeof(Robust.Server.Physics.GridFixtureSystem).Assembly
        .GetType("Robust.Server.BaseServer")?.GetField("TickUsage", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Histogram;

    private static readonly Assembly Engine = typeof(EntitySessionEventArgs).Assembly;
    private static readonly Type? SessionMessage = Engine.GetType("Robust.Shared.GameObjects.EntitySessionMessage`1");
    private static readonly Type? BuiWrap = Engine.GetType("Robust.Shared.GameObjects.BaseBoundUIWrapMessage");
    private static readonly FieldInfo? BuiMessage = BuiWrap?.GetField("Message");
    private static readonly FieldInfo? BuiEntity = BuiWrap?.GetField("Entity");

    private readonly WGDispatchClock _clock = new();
    private readonly WGDispatchClock.Charge _record;
    private readonly Dictionary<Type, Kind> _kinds = new();
    private readonly Dictionary<Type, string> _names = new();
    private readonly Dictionary<KeyFunctionId, string> _inputs = new();
    private readonly Dictionary<string, Histogram.Child> _children = new();
    private readonly Dictionary<string, (int Count, double Seconds)> _totals = new();
    private readonly Dictionary<Type, PropertyInfo?> _senders = new();

    private Histogram.Child? _entityNet;
    private double _entityNetSum = double.NaN;
    private bool _hooked;
    private float _slowMs;

    /// <summary>The slowest message charged this tick, read by <see cref="WGTickWatchSystem"/>.</summary>
    public (GameTick Tick, string Label, double Ms) Slowest { get; private set; }

    /// <summary>Messages and seconds charged per label since startup.</summary>
    public IReadOnlyDictionary<string, (int Count, double Seconds)> Totals => _totals;

    public WGNetDispatchWatchSystem()
    {
        _record = Record;
    }

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(WGTickWatchSystem));

        _entityNet = EngineUsage?.WithLabels("EntityNet");
        if (_entityNet == null || SessionMessage == null || BuiMessage == null)
            Log.Warning("The engine's tick timing or message types weren't found; client message times will be partial.");

        Subs.CVar(_cfg, WGNetDispatchCVars.SlowMessageMs, value => _slowMs = value, true);
        Subs.CVar(_cfg, WGNetDispatchCVars.Enabled, SetHooked, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetHooked(false);
    }

    private void SetHooked(bool hooked)
    {
        if (hooked == _hooked || EntityManager.EntityNetManager is not { } net)
            return;

        _hooked = hooked;
        if (hooked)
            net.ReceivedSystemMessage += OnReceived;
        else
            net.ReceivedSystemMessage -= OnReceived;

        _clock.EndTick(null, _record);
        _entityNetSum = double.NaN;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_hooked)
            return;

        double? entityNet = null;
        if (_entityNet != null)
        {
            var sum = _entityNet.Sum;
            if (!double.IsNaN(_entityNetSum))
                entityNet = sum - _entityNetSum;
            _entityNetSum = sum;
        }

        _clock.EndTick(entityNet, _record);
    }

    private void OnReceived(object? sender, object message)
    {
        var type = message.GetType();
        if (!_kinds.TryGetValue(type, out var kind))
            _kinds[type] = kind = Classify(type);

        if (kind == Kind.Wrapper)
            _clock.Finished(System.Diagnostics.Stopwatch.GetTimestamp(), message, _record);
        else
            _clock.Raised(Label(message, kind), message);
    }

    private static Kind Classify(Type type)
    {
        if (SessionMessage != null && type.IsGenericType && type.GetGenericTypeDefinition() == SessionMessage)
            return Kind.Wrapper;

        if (BuiWrap != null && BuiWrap.IsAssignableFrom(type))
            return Kind.Bui;

        return type == typeof(FullInputCmdMessage) ? Kind.Input : Kind.Plain;
    }

    private string Label(object message, Kind kind)
    {
        switch (kind)
        {
            case Kind.Bui when BuiMessage?.GetValue(message) is { } inner:
                return Name(inner.GetType(), "BUI ");
            case Kind.Input:
                var id = ((FullInputCmdMessage) message).InputFunctionId;
                if (!_inputs.TryGetValue(id, out var input))
                    _inputs[id] = input = "Input " + _players.KeyMap.KeyFunctionName(id).FunctionName;
                return input;
            default:
                return Name(message.GetType(), string.Empty);
        }
    }

    private string Name(Type type, string prefix)
    {
        if (!_names.TryGetValue(type, out var name))
            _names[type] = name = prefix + type.Name;
        return name;
    }

    private void Record(string label, double seconds, object message, object? wrapper)
    {
        if (!_children.TryGetValue(label, out var child))
            _children[label] = child = DispatchSeconds.WithLabels(label);
        child.Observe(seconds);

        var total = _totals.GetValueOrDefault(label);
        _totals[label] = (total.Count + 1, total.Seconds + seconds);

        var ms = seconds * 1000;
        var tick = _timing.CurTick;
        if (Slowest.Tick != tick || ms > Slowest.Ms)
            Slowest = (tick, label, ms);

        if (_slowMs > 0f && ms >= _slowMs)
            Log.Warning($"Slow client message {label}: {ms:F1} ms, from {Sender(wrapper)}{Target(message)}.");
    }

    private string Sender(object? wrapper)
    {
        if (wrapper == null)
            return "unknown";

        var type = wrapper.GetType();
        if (!_senders.TryGetValue(type, out var property))
            _senders[type] = property = type.GetProperty("EventArgs");

        return property?.GetValue(wrapper) is EntitySessionEventArgs args ? args.SenderSession.Name : "unknown";
    }

    private string Target(object message)
    {
        var net = message switch
        {
            FullInputCmdMessage input => input.Uid,
            _ when BuiEntity != null && BuiWrap!.IsInstanceOfType(message) => (NetEntity) BuiEntity.GetValue(message)!,
            _ => NetEntity.Invalid,
        };

        return TryGetEntity(net, out var uid) ? $" on {ToPrettyString(uid)}" : string.Empty;
    }

    private enum Kind : byte
    {
        Plain,
        Wrapper,
        Bui,
        Input,
    }
}
