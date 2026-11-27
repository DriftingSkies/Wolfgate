using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.Performance;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>Server cvars for buying a ship in two steps.</summary>
[CVarDefs]
public sealed class WGShipyardCVars
{
    /// <summary>
    /// Whether a ship bought at a shipyard console, or from a trader's used lot, has its file parsed off the main thread
    /// before the purchase goes through. Parsing a big hull in the purchase itself held the tick for most of a second.
    /// </summary>
    public static readonly CVarDef<bool> AsyncPurchase =
        CVarDef.Create("wg.shipyard.async_purchase", true, CVar.SERVERONLY);
}

public sealed partial class ShipyardSystem
{
    [Dependency] private IResourceManager _wgResources = default!;

    private static readonly Histogram WGPurchaseSeconds = Metrics.CreateHistogram(
        "wg_shipyard_purchase_seconds",
        "Main-thread time of a shipyard console purchase, by step: loading the hull, then the purchase itself",
        new HistogramConfiguration
        {
            LabelNames = ["step"],
            Buckets = Histogram.ExponentialBuckets(0.005, 2, 10),
        });

    /// <summary>
    /// A purchase waiting for its hull: first for the parse on a worker, then for the tick after the hull is loaded,
    /// when it runs again as it came in.
    /// </summary>
    private sealed class WGPendingPurchase
    {
        public required ShipyardConsoleComponent Component;
        public required ShipyardConsolePurchaseMessage Message;
        public required ResPath Path;
        public required Task<MappingDataNode?> Parsing;
        public required long Ordered;

        /// <summary>The hull on the shipyard map, once loaded; the purchase takes it on the next tick.</summary>
        public EntityUid? Hull;

        public int Entities;
        public double WaitMs;
        public double LoadMs;
        public double LoadGcMs;
    }

    private readonly Dictionary<EntityUid, WGPendingPurchase> _wgPendingPurchases = new();
    private readonly List<EntityUid> _wgParsedConsoles = new();

    /// <summary>The loaded hull a purchase running again takes in place of loading one.</summary>
    private (ResPath Path, EntityUid Grid)? _wgPreparedHull;

    /// <summary>Whether a purchase is waiting for its hull at this console.</summary>
    public bool WGIsPreparing(EntityUid console) => _wgPendingPurchases.ContainsKey(console);

    /// <summary>The hull loaded for the purchase waiting at this console, if it is loaded yet.</summary>
    internal EntityUid? WGPreparedHull(EntityUid console)
        => _wgPendingPurchases.TryGetValue(console, out var pending) ? pending.Hull : null;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        WGFinishPurchases();
    }

    /// <summary>
    /// Whether a purchase waits for its hull to be parsed. If so it has returned; the parse is started, and the purchase
    /// runs again from the top once the hull is loaded, so every check (card, access, funds, voucher) is made on the state
    /// then.
    /// </summary>
    private bool WGDeferPurchase(EntityUid console, ShipyardConsoleComponent component,
        ShipyardConsolePurchaseMessage args, EntityUid player, VesselPrototype vessel)
    {
        if (!_configManager.GetCVar(WGShipyardCVars.AsyncPurchase)
            || _wgPreparedHull is { } prepared && prepared.Path == vessel.ShuttlePath)
            return false;

        if (_wgPendingPurchases.ContainsKey(console))
        {
            ConsolePopup(player, Loc.GetString("wg-shipyard-preparing-busy"));
            PlayDenySound(player, console, component);
            return true;
        }

        _wgPendingPurchases[console] = new WGPendingPurchase
        {
            Component = component,
            Message = args,
            Path = vessel.ShuttlePath,
            Parsing = WGGridFileReader.ReadAsync(_wgResources, vessel.ShuttlePath),
            Ordered = Stopwatch.GetTimestamp(),
        };
        ConsolePopup(player, Loc.GetString("wg-shipyard-preparing", ("vessel", vessel.Name)));
        return true;
    }

    /// <summary>
    /// Runs again each purchase whose hull was loaded last tick, or else loads one parsed hull, so loading a hull and the
    /// rest of a purchase never share a tick.
    /// </summary>
    private void WGFinishPurchases()
    {
        if (_wgPendingPurchases.Count == 0)
            return;

        _wgParsedConsoles.Clear();
        EntityUid? toLoad = null;
        foreach (var (console, pending) in _wgPendingPurchases)
        {
            if (pending.Hull != null)
                _wgParsedConsoles.Add(console);
            else if (toLoad == null && pending.Parsing.IsCompleted)
                toLoad = console;
        }

        foreach (var console in _wgParsedConsoles)
        {
            WGCompletePurchase(console);
        }

        if (_wgParsedConsoles.Count == 0 && toLoad is { } next)
            WGPrepareHull(next);
    }

    /// <summary>Loads a parsed hull onto the shipyard map for the purchase at this console.</summary>
    private void WGPrepareHull(EntityUid console)
    {
        var pending = _wgPendingPurchases[console];
        if (TerminatingOrDeleted(console)
            || pending.Message.Actor is not { Valid: true } player
            || TerminatingOrDeleted(player))
        {
            _wgPendingPurchases.Remove(console);
            return;
        }

        // Completed by now, so reading it doesn't wait.
        if (!pending.Parsing.IsCompletedSuccessfully || pending.Parsing.GetAwaiter().GetResult() is not { } data)
        {
            Log.Error($"Could not read {pending.Path} for a purchase at {ToPrettyString(console)}: {pending.Parsing.Exception?.InnerException}");
            WGFailPurchase(console, pending, player);
            return;
        }

        SetupShipyardIfNeeded();
        var gc = GC.GetTotalPauseDuration();
        var start = Stopwatch.GetTimestamp();
        if (ShipyardMap is not { } map
            || !WGGridFileReader.TryLoad(_mapLoader, data, pending.Path.ToString(), map, out var grid, out var entities,
                new Vector2(500f + _shuttleIndex, 1f)))
        {
            Log.Error($"Could not load {pending.Path} for a purchase at {ToPrettyString(console)}");
            WGFailPurchase(console, pending, player);
            return;
        }

        // The purchase moves the slot on again when it takes the hull. That leaves a gap; a hull loaded meanwhile by
        // anything else would otherwise land on top of this one.
        _shuttleIndex += grid.Value.Comp.LocalAABB.Width + ShuttleSpawnBuffer;

        pending.Hull = grid.Value.Owner;
        pending.Entities = entities;
        pending.LoadMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        pending.LoadGcMs = (GC.GetTotalPauseDuration() - gc).TotalMilliseconds;
        pending.WaitMs = Stopwatch.GetElapsedTime(pending.Ordered, start).TotalMilliseconds;
        WGPurchaseSeconds.WithLabels("load").Observe(pending.LoadMs / 1000);
    }

    /// <summary>Runs the purchase at this console again, with its loaded hull at hand.</summary>
    private void WGCompletePurchase(EntityUid console)
    {
        var pending = _wgPendingPurchases[console];
        _wgPendingPurchases.Remove(console);
        var hull = pending.Hull!.Value;

        if (TerminatingOrDeleted(console)
            || pending.Message.Actor is not { Valid: true } player
            || TerminatingOrDeleted(player))
        {
            if (!TerminatingOrDeleted(hull))
                QueueDel(hull);
            return;
        }

        if (TerminatingOrDeleted(hull))
        {
            ConsolePopup(player, Loc.GetString("wg-shipyard-preparing-failed"));
            PlayDenySound(player, console, pending.Component);
            return;
        }

        var gc = GC.GetTotalPauseDuration();
        var start = Stopwatch.GetTimestamp();
        _wgPreparedHull = (pending.Path, hull);
        try
        {
            OnPurchaseMessage(console, pending.Component, pending.Message);
        }
        finally
        {
            // A purchase turned away before it took the hull leaves it on the shipyard map.
            if (_wgPreparedHull is { } unused && !TerminatingOrDeleted(unused.Grid))
                QueueDel(unused.Grid);
            _wgPreparedHull = null;
        }

        var purchaseMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var purchaseGcMs = (GC.GetTotalPauseDuration() - gc).TotalMilliseconds;
        WGPurchaseSeconds.WithLabels("purchase").Observe(purchaseMs / 1000);

        if (!TerminatingOrDeleted(hull) && HasComp<ShuttleDeedComponent>(hull))
        {
            Log.Info($"Bought {pending.Path} at {ToPrettyString(console)}: ready after {pending.WaitMs:0} ms, load {pending.LoadMs:0} ms "
                     + $"({pending.Entities} entities, GC {pending.LoadGcMs:0} ms), purchase {purchaseMs:0} ms (GC {purchaseGcMs:0} ms).");
        }
    }

    /// <summary>Tells the buyer the ship couldn't be prepared and drops the order.</summary>
    private void WGFailPurchase(EntityUid console, WGPendingPurchase pending, EntityUid player)
    {
        _wgPendingPurchases.Remove(console);
        ConsolePopup(player, Loc.GetString("wg-shipyard-preparing-failed"));
        PlayDenySound(player, console, pending.Component);
    }

    /// <summary>
    /// Puts a hull on the shipyard map: the one loaded for a purchase running again, otherwise loaded from its file.
    /// </summary>
    private bool WGLoadHull(ResPath path, Vector2 offset, [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        if (_wgPreparedHull is { } prepared && prepared.Path == path
            && TryComp<MapGridComponent>(prepared.Grid, out var gridComp))
        {
            // Used once: a second load in the same purchase reads the file as before.
            _wgPreparedHull = null;
            grid = (prepared.Grid, gridComp);
            return true;
        }

        return _mapLoader.TryLoadGrid(ShipyardMap!.Value, path, out grid, offset: offset);
    }
}
