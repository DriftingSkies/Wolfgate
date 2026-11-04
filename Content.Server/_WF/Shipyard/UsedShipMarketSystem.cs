using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Traders;
using Content.Shared.GameTicking;
using Robust.Shared.Audio.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WF.Shipyard;

/// <summary>
/// One ship bought back from a customer, kept as YAML until someone buys it again.
/// </summary>
public sealed class UsedShipListing
{
    public int Id;

    /// <summary>
    /// Name the ship was sold under, kept through the resale.
    /// </summary>
    public string ShipName = string.Empty;

    /// <summary>
    /// Vessel prototype the hull was built from, if the grid still said so.
    /// </summary>
    public ProtoId<VesselPrototype>? DesignId;

    public string DesignName = string.Empty;

    public string SellerName = string.Empty;

    /// <summary>
    /// What the console booked for the ship: appraisal after the sale rate, before taxes.
    /// </summary>
    public int SaleValue;

    /// <summary>
    /// What it goes back on the lot for.
    /// </summary>
    public int Price;

    public TimeSpan SoldAt;

    public TimeSpan AvailableAt;

    /// <summary>
    /// The grid's YAML being written on a worker, done long before the listing can be bought.
    /// </summary>
    public Task<string?>? Writing;

    private string? _data;

    /// <summary>
    /// Whether the grid's YAML is written and the listing can go on the lot.
    /// </summary>
    public bool Ready => Writing == null || Writing.IsCompletedSuccessfully && Writing.GetAwaiter().GetResult() != null;

    /// <summary>
    /// Whether writing the grid's YAML failed, which loses the ship.
    /// </summary>
    public bool WriteFailed => Writing is { IsCompleted: true } && !Ready;

    /// <summary>
    /// The grid, exactly as it was when it was bought. Empty if writing it failed.
    /// </summary>
    public string Data
    {
        get => _packed != null ? Unpack(_packed) : _data ??= Writing?.GetAwaiter().GetResult() ?? string.Empty;
        set => _data = value;
    }

    private byte[]? _packed;

    /// <summary>
    /// Replaces the written YAML with its gzip bytes, so a long round does not hold every sold ship as a large string.
    /// </summary>
    public bool Pack()
    {
        if (_packed != null || Writing is not { IsCompletedSuccessfully: true })
            return false;

        var text = Writing.GetAwaiter().GetResult();
        if (text == null)
            return false;

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }

        _packed = output.ToArray();
        _data = null;
        Writing = null;
        return true;
    }

    private static string Unpack(byte[] packed)
    {
        using var input = new MemoryStream(packed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Whether the lot has already been told about it.
    /// </summary>
    public bool Announced;
}

/// <summary>
/// Raised when a used ship finishes its stint out back and goes on the lot.
/// </summary>
[ByRefEvent]
public record struct UsedShipListedEvent(UsedShipListing Listing);

/// <summary>
/// Keeps the round's used ship lot: copies every ship sold on a marked station, and hands the
/// copies back out when someone buys one. In memory only, cleared on round restart.
/// </summary>
public sealed partial class UsedShipMarketSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private WFHullRetireSystem _retire = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private UsedShipReinitSystem _reinit = default!;

    private readonly List<UsedShipListing> _listings = new();
    private int _nextId = 1;

    /// <summary>
    /// Markup on what the seller was paid, used when a sale does not come from a salesman.
    /// </summary>
    public const float DefaultMarkup = 0.05f;

    /// <summary>
    /// Most ships kept on the lot at once; the oldest listing goes when another is sold.
    /// </summary>
    public const int MaxListings = 50;

    /// <summary>
    /// How long a ship waits before it goes on the lot, when a sale does not come from a salesman.
    /// </summary>
    public static readonly TimeSpan DefaultRelistDelay = TimeSpan.FromMinutes(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipSoldEvent>(OnShipSold);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _listings.Clear();
        _nextId = 1;
    }

    #region Listings

    /// <summary>
    /// Everything on the lot right now, cheapest first.
    /// </summary>
    public List<UsedShipListing> GetAvailable()
    {
        var now = _timing.CurTime;
        return _listings.Where(l => l.AvailableAt <= now && l.Ready).OrderBy(l => l.Price).ToList();
    }

    /// <summary>
    /// A listing that is on the lot right now. Ones still out back do not count.
    /// </summary>
    public bool TryGetAvailableListing(int id, [NotNullWhen(true)] out UsedShipListing? listing)
    {
        listing = _listings.FirstOrDefault(l => l.Id == id && l.AvailableAt <= _timing.CurTime && l.Ready);
        return listing != null;
    }

    public void RemoveListing(UsedShipListing listing)
    {
        _listings.Remove(listing);
    }

    /// <summary>
    /// The whole lot, sold and waiting alike. For tests and admin tooling.
    /// </summary>
    public IReadOnlyList<UsedShipListing> Listings => _listings;

    #endregion

    #region Capture

    private void OnShipSold(ref ShipSoldEvent args)
    {
        if (!HasComp<UsedShipMarketComponent>(args.Station))
            return;

        var markup = DefaultMarkup;
        var delay = DefaultRelistDelay;

        // The lot's own salesman sets the terms, wherever on the station the ship was actually sold.
        var query = EntityQueryEnumerator<TraderUsedShipsComponent>();
        while (query.MoveNext(out var uid, out var salesman))
        {
            if (_station.GetOwningStation(uid) != args.Station)
                continue;

            markup = salesman.UsedShipMarkup;
            delay = salesman.RelistDelay;
            break;
        }

        TryCapture(args.Shuttle, args.Console, args.Appraisal, markup, delay, out _);
    }

    /// <summary>
    /// Copies a ship that has just been sold and puts it out back, over the ticks after the sale. The live grid is only
    /// parked, so its own deletion still runs every cleanup bound to the old owner; the copy is stripped when it is loaded.
    /// </summary>
    public bool TryCapture(EntityUid shuttle,
        EntityUid console,
        int appraisal,
        float markup,
        TimeSpan relistDelay,
        [NotNullWhen(true)] out UsedShipListing? listing)
    {
        listing = null;

        var shipName = Name(shuttle);
        var seller = string.Empty;
        if (_shipyard.TryGetShipDeedInfo(shuttle, out var deedName, out var deedOwner))
        {
            if (!string.IsNullOrWhiteSpace(deedName))
                shipName = deedName;

            seller = deedOwner;
        }

        ProtoId<VesselPrototype>? designId = null;
        var designName = Loc.GetString("wf-used-ship-unknown-design");
        if (TryComp<VesselComponent>(shuttle, out var vessel)
            && _proto.TryIndex<VesselPrototype>(vessel.VesselId, out var design))
        {
            designId = design.ID;
            designName = design.Name;
        }

        // Pets, borgs and mechs are not map-savable, but the console already refuses a sale with a
        // player aboard, so whatever is left can ride along with the hull.
        var carry = GetUnsavableAboard(shuttle);
        if (carry.Count > 0)
            Log.Info($"Copying {ToPrettyString(shuttle)} for resale with {carry.Count} crew or machines aboard.");

        // A docked hull's joints and docks point at the station; the copy cannot carry them and a
        // dangling reference makes the whole load fail. Retiring undocks it first.
        var writing = _retire.RetireWithCopy(shuttle, carry);

        var saleValue = _shipyard.GetPostSaleRateBill(console, appraisal);
        var now = _timing.CurTime;

        listing = new UsedShipListing
        {
            Id = _nextId++,
            ShipName = shipName,
            DesignId = designId,
            DesignName = designName,
            SellerName = string.IsNullOrWhiteSpace(seller) ? Loc.GetString("wf-used-ship-unknown-seller") : seller,
            SaleValue = saleValue,
            Price = (int) Math.Ceiling(saleValue * (1f + Math.Max(0f, markup))),
            SoldAt = now,
            AvailableAt = now + relistDelay,
            Writing = writing,
        };

        while (_listings.Count >= MaxListings)
        {
            Log.Info($"Used lot is full; dropped {_listings[0].ShipName}.");
            _listings.RemoveAt(0);
        }

        _listings.Add(listing);
        Log.Info($"Captured {shipName} ({designName}) for the used lot at {listing.Price}, available at {listing.AvailableAt}.");
        return true;
    }

    /// <summary>
    /// Everything aboard a grid the map loader will not write out and somebody would miss: borgs,
    /// mechs, drones, pets. The copy comes back without them, so a sale has to be refused first.
    /// </summary>
    public List<EntityUid> GetUnsavableAboard(EntityUid grid)
    {
        // Players are never copied; the sale rules keep them off the hull before this runs anyway.

        var found = new List<EntityUid>();
        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);

        while (pending.TryDequeue(out var parent))
        {
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (MetaData(child).EntityPrototype is { MapSavable: false } && !IsTransient(child))
                {
                    // A player isn't copied, and takes everything inside them with them.
                    if (HasComp<ActorComponent>(child))
                        continue;

                    found.Add(child);
                }

                // A carried entity is saved with its containers, so whatever they hold must be carried too.
                pending.Enqueue(child);
            }
        }

        return found;
    }

    /// <summary>
    /// Whether an entity is aboard only for a moment: a playing sound or a timed effect. Every
    /// humming computer parents an unsavable audio stream to the grid, and nobody can carry one off.
    /// </summary>
    private bool IsTransient(EntityUid uid)
    {
        return HasComp<AudioComponent>(uid) || HasComp<TimedDespawnComponent>(uid);
    }

    #endregion

    #region Buying

    /// <summary>
    /// Loads a listing's grid and docks it to a station, exactly as a shipyard purchase would.
    /// Leaves the deed to the caller, which needs the buyer's session.
    /// </summary>
    public bool TryLoadListing(UsedShipListing listing, EntityUid station, [NotNullWhen(true)] out EntityUid? shuttle)
    {
        shuttle = null;

        if (!_shipyard.TryAddSavedShip(listing.Data, out var grid))
            return false;

        _shipyard.StripForResale(grid.Value);
        _reinit.ReinitLoadedShip(grid.Value);

        if (!_shipyard.TryDockLoadedShuttle(station, grid.Value))
        {
            QueueDel(grid.Value);
            return false;
        }

        shuttle = grid;
        return true;
    }

    /// <summary>
    /// The design a listing was built from, if the prototype is still around.
    /// </summary>
    public VesselPrototype? GetDesign(UsedShipListing listing)
    {
        if (listing.DesignId is not { } id)
            return null;

        return _proto.TryIndex(id, out var design) ? design : null;
    }

    /// <summary>
    /// The station a trader standing on a grid belongs to.
    /// </summary>
    public EntityUid? GetStation(EntityUid uid)
    {
        return _station.GetOwningStation(uid);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_listings.Count == 0)
            return;

        for (var i = _listings.Count - 1; i >= 0; i--)
        {
            if (!_listings[i].WriteFailed)
                continue;

            Log.Warning($"Dropped {_listings[i].ShipName} from the used lot; its copy could not be written.");
            _listings.RemoveAt(i);
        }

        foreach (var listing in _listings)
        {
            if (listing.Pack())
                break;
        }

        var now = _timing.CurTime;
        foreach (var listing in _listings)
        {
            if (listing.Announced || listing.AvailableAt > now || !listing.Ready)
                continue;

            listing.Announced = true;

            var ev = new UsedShipListedEvent(listing);
            RaiseLocalEvent(ref ev);
        }
    }
}
