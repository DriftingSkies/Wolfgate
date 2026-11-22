using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.Performance;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Map.Components;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;

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

    /// <summary>A purchase waiting for its hull's file to be parsed, to run again as it came in once it is.</summary>
    private sealed class WGPendingPurchase
    {
        public required ShipyardConsoleComponent Component;
        public required ShipyardConsolePurchaseMessage Message;
        public required ResPath Path;
        public required Task<MappingDataNode?> Parsing;
    }

    private readonly Dictionary<EntityUid, WGPendingPurchase> _wgPendingPurchases = new();
    private readonly List<EntityUid> _wgParsedConsoles = new();

    /// <summary>The parsed hull a purchase running again loads in place of reading its file.</summary>
    private (ResPath Path, MappingDataNode Data)? _wgParsedHull;

    /// <summary>Whether a purchase is waiting for its hull at this console.</summary>
    public bool WGIsPreparing(EntityUid console) => _wgPendingPurchases.ContainsKey(console);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        WGFinishPurchases();
    }

    /// <summary>
    /// Whether a purchase waits for its hull to be parsed. If so it has returned; the parse is started, and the purchase
    /// runs again from the top once it is done, so every check (card, access, funds, voucher) is made on the state then.
    /// </summary>
    private bool WGDeferPurchase(EntityUid console, ShipyardConsoleComponent component,
        ShipyardConsolePurchaseMessage args, EntityUid player, VesselPrototype vessel)
    {
        if (!_configManager.GetCVar(WGShipyardCVars.AsyncPurchase)
            || _wgParsedHull is { } parsed && parsed.Path == vessel.ShuttlePath)
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
        };
        ConsolePopup(player, Loc.GetString("wg-shipyard-preparing", ("vessel", vessel.Name)));
        return true;
    }

    /// <summary>Runs again each purchase whose hull is parsed, with the parsed hull at hand.</summary>
    private void WGFinishPurchases()
    {
        if (_wgPendingPurchases.Count == 0)
            return;

        _wgParsedConsoles.Clear();
        foreach (var (console, pending) in _wgPendingPurchases)
        {
            if (pending.Parsing.IsCompleted)
                _wgParsedConsoles.Add(console);
        }

        foreach (var console in _wgParsedConsoles)
        {
            if (!_wgPendingPurchases.Remove(console, out var pending)
                || TerminatingOrDeleted(console)
                || pending.Message.Actor is not { Valid: true } player
                || TerminatingOrDeleted(player))
                continue;

            // Completed by now, so reading it doesn't wait.
            if (!pending.Parsing.IsCompletedSuccessfully || pending.Parsing.GetAwaiter().GetResult() is not { } data)
            {
                Log.Error($"Could not read {pending.Path} for a purchase at {ToPrettyString(console)}: {pending.Parsing.Exception?.InnerException}");
                ConsolePopup(player, Loc.GetString("wg-shipyard-preparing-failed"));
                PlayDenySound(player, console, pending.Component);
                continue;
            }

            _wgParsedHull = (pending.Path, data);
            try
            {
                OnPurchaseMessage(console, pending.Component, pending.Message);
            }
            finally
            {
                _wgParsedHull = null;
            }
        }
    }

    /// <summary>
    /// Loads a hull onto the shipyard map: from the parsed data when a purchase waited for it, otherwise from its file.
    /// </summary>
    private bool WGLoadHull(ResPath path, Vector2 offset, [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        if (_wgParsedHull is { } parsed && parsed.Path == path)
        {
            // Used once: a second load in the same purchase reads the file as before.
            _wgParsedHull = null;
            return WGGridFileReader.TryLoad(_mapLoader, parsed.Data, path, ShipyardMap!.Value, out grid, offset);
        }

        return _mapLoader.TryLoadGrid(ShipyardMap!.Value, path, out grid, offset: offset);
    }
}
