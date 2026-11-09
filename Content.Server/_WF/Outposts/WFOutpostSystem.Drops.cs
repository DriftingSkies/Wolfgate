using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets.Bounds;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Random;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Drops: a save brought down for an Outpost crew, with no founding outpost. Each drop is read as soon as it is queued,
/// then lands, once its world has finished preloading, on open unclaimed ground somewhere on it; one lands a second.
/// The caller charges for it and hears back through its callback.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFPlanetBoundsSystem _bounds = default!;
    [Dependency] private WFPlanetPreloadSystem _preload = default!;

    /// <summary>Sites tried for a drop before it gives up.</summary>
    private const int DropAttempts = 32;

    /// <summary>How far from an unbounded world's middle a drop may land, in tiles.</summary>
    private const float UnboundedDropRadius = 150f;

    /// <summary>Share of a bounded world's radius a drop may land within, clear of its edge.</summary>
    private const float BoundedDropShare = 0.6f;

    /// <summary>Tiles along a biome chunk's side (SharedBiomeSystem.ChunkSize).</summary>
    private const int BiomeChunkSize = 8;

    private static readonly TimeSpan DropInterval = TimeSpan.FromSeconds(1);

    private sealed class PendingDrop
    {
        public NetUserId Owner;
        public string OwnerName = string.Empty;

        /// <summary>The save slot it comes from; null for a preset, whose YAML is read when it is queued.</summary>
        public int? Slot;
        public EntityUid Ground;
        public Action<EntityUid?, string> Done = default!;
        public string Name = string.Empty;
        public string? Yaml;
        public bool Reading;
    }

    private readonly List<PendingDrop> _drops = new();
    private TimeSpan _nextDrop;

    /// <summary>Outposts standing, abandoned ones included, and drops on their way down.</summary>
    public int CountLive()
    {
        var count = _drops.Count;
        var query = EntityQueryEnumerator<WFOutpostComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!TerminatingOrDeleted(uid))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Whether an account's save could be dropped now: outposts on, under the live cap, no outpost of theirs standing or
    /// on its way, and the save there and not used this round. The slots must have been read (<see cref="GetSlots"/>).
    /// </summary>
    public bool CanDrop(NetUserId owner, int slot, out string reason)
    {
        if (!CanDropAny(owner, out reason))
            return false;

        if (GetSlots(owner) is not { } slots)
        {
            reason = "wf-outpost-save-busy";
            return false;
        }

        if (slot < 0 || slot >= slots.Length || slots[slot] is not { } save)
        {
            reason = "wf-outpost-load-empty";
            return false;
        }

        if (IsInUse(save))
        {
            reason = "wf-outpost-load-in-use";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Whether a preset could be dropped for an account now: outposts on, under the live cap, none of theirs standing.</summary>
    public bool CanDropPreset(NetUserId owner, string preset, out string reason)
    {
        if (!_proto.HasIndex<WFOutpostPresetPrototype>(preset))
        {
            reason = "wf-outpost-preset-unknown";
            return false;
        }

        return CanDropAny(owner, out reason);
    }

    private bool CanDropAny(NetUserId owner, out string reason)
    {
        if (!_cfg.GetCVar(OutpostCVars.Enabled))
        {
            reason = "wf-outpost-found-disabled";
            return false;
        }

        if (CountLive() >= _cfg.GetCVar(OutpostCVars.MaxLive))
        {
            reason = "wf-outpost-drop-cap";
            return false;
        }

        if (TryGetOutpost(owner, out _) || _drops.Any(drop => drop.Owner == owner))
        {
            reason = "wf-outpost-found-already-own";
            return false;
        }

        if (IsBusy(owner))
        {
            reason = "wf-outpost-save-busy";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Queues a preset to drop onto a world's ground as an account's outpost, as <see cref="TryQueueDrop"/> does a save.</summary>
    public bool TryQueuePresetDrop(NetUserId owner, string ownerName, string presetId, EntityUid ground,
        Action<EntityUid?, string> done, out string reason)
    {
        if (!CanDropPreset(owner, presetId, out reason))
            return false;

        if (!HasComp<BiomeComponent>(ground) || !HasComp<MapGridComponent>(ground) || !IsGroundLayer(ground))
        {
            reason = "wf-outpost-drop-no-site";
            return false;
        }

        var preset = _proto.Index<WFOutpostPresetPrototype>(presetId);
        if (!TryReadPreset(preset, out var yaml))
        {
            reason = "wf-outpost-load-failed";
            return false;
        }

        _busy.Add(owner);
        _drops.Add(new PendingDrop
        {
            Owner = owner, OwnerName = ownerName, Ground = ground, Done = done, Name = Loc.GetString(preset.Name), Yaml = yaml,
        });
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"Outpost preset {presetId} for {owner} queued to drop onto {ToPrettyString(ground):map}");
        return true;
    }

    /// <summary>
    /// Queues a save to drop onto a world's ground as its owner's outpost. <paramref name="done"/> is called once, with
    /// the new outpost or null and a loc id, unless the round ends first.
    /// </summary>
    public bool TryQueueDrop(NetUserId owner, string ownerName, int slot, EntityUid ground,
        Action<EntityUid?, string> done, out string reason)
    {
        if (!CanDrop(owner, slot, out reason))
            return false;

        if (!HasComp<BiomeComponent>(ground) || !HasComp<MapGridComponent>(ground) || !IsGroundLayer(ground))
        {
            reason = "wf-outpost-drop-no-site";
            return false;
        }

        _busy.Add(owner);
        _drops.Add(new PendingDrop { Owner = owner, OwnerName = ownerName, Slot = slot, Ground = ground, Done = done });
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"Outpost save slot {slot} of {owner} queued to drop onto {ToPrettyString(ground):map}");
        return true;
    }

    private void UpdateDrops()
    {
        if (_drops.Count == 0)
            return;

        foreach (var drop in _drops.Where(drop => drop.Yaml == null && !drop.Reading).ToList())
        {
            drop.Reading = true;
            _ = ReadDrop(drop);
        }

        var now = _timing.CurTime;
        if (now < _nextDrop)
            return;

        var ready = _drops.FirstOrDefault(drop => drop.Yaml != null && !_preload.IsPreloading(drop.Ground));
        if (ready == null)
            return;

        _nextDrop = now + DropInterval;
        LandDrop(ready);
    }

    private async Task ReadDrop(PendingDrop drop)
    {
        try
        {
            if (drop.Slot is not { } slot || await _db.GetOutpostSaveDataAsync(drop.Owner, slot) is not { } row)
            {
                FinishDrop(drop, null, "wf-outpost-load-empty");
                return;
            }

            if (IsInUse(row.Save))
            {
                FinishDrop(drop, null, "wf-outpost-load-in-use");
                return;
            }

            drop.Name = row.Save.Name;
            drop.Yaml = await Task.Run(() => Unpack(row.Data));
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read outpost save slot {drop.Slot} of {drop.Owner} for a drop: {e}");
            FinishDrop(drop, null, "wf-outpost-load-failed");
        }
    }

    /// <summary>Stages a read drop and brings it down on the first clear site found, or gives it up.</summary>
    private void LandDrop(PendingDrop drop)
    {
        if (TerminatingOrDeleted(drop.Ground) || !TryComp<BiomeComponent>(drop.Ground, out var biome)
            || !TryComp<MapGridComponent>(drop.Ground, out var groundGrid))
        {
            FinishDrop(drop, null, "wf-outpost-drop-no-site");
            return;
        }

        if (!MayFound(drop.Owner, null, drop.Ground, out var refusal))
        {
            FinishDrop(drop, null, refusal);
            return;
        }

        if (!TryStageSave(drop.Yaml!, $"outpost drop {drop.Name}", out var staged, out var console))
        {
            Log.Error($"Outpost save slot {drop.Slot} of {drop.Owner} failed to stage for a drop.");
            FinishDrop(drop, null, "wf-outpost-load-failed");
            return;
        }

        var tiles = new List<Vector2i>();
        var enumerator = _maps.GetAllTilesEnumerator(staged, staged.Comp);
        while (enumerator.MoveNext(out var tile))
        {
            tiles.Add(tile.Value.GridIndices);
        }

        var consoleTile = _maps.TileIndicesFor(staged, staged.Comp, Transform(console).Coordinates);
        if (!TryFindDropSite(drop.Owner, (drop.Ground, biome, groundGrid), tiles, consoleTile, out var shift, staged))
        {
            Del(staged);
            FinishDrop(drop, null, "wf-outpost-drop-no-site");
            return;
        }

        var origin = Vector2.Transform((Vector2) shift, _transform.GetWorldMatrix(drop.Ground));
        Settle(staged, console, drop.Owner, drop.OwnerName, drop.Ground, drop.Slot, drop.Name, origin,
            _transform.GetWorldRotation(drop.Ground));
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"Outpost save {drop.Name} (slot {drop.Slot}) of {drop.Owner} dropped as {ToPrettyString(staged):grid}");
        FinishDrop(drop, staged, string.Empty);
    }

    /// <summary>
    /// A shift that puts a save on a lot the world kept for outposts, or else at a random spot of the world, with every
    /// tile of it on open, unclaimed ground and its claim clear of reserved ground. Chunks under each try are generated
    /// first.
    /// </summary>
    private bool TryFindDropSite(NetUserId owner, Entity<BiomeComponent, MapGridComponent> ground, List<Vector2i> tiles, Vector2i consoleTile,
        out Vector2i shift, EntityUid? staged = null)
    {
        if (_bounds.TryGetBounds(ground, out var centre, out var radius))
            radius *= BoundedDropShare;
        else
        {
            centre = Vector2.Zero;
            radius = UnboundedDropRadius;
        }

        // The lots the world kept for outposts first, centred on the save's middle, unless a module keeps them for others.
        var lots = new WFOutpostLotsEvent(owner, ground, staged);
        RaiseLocalEvent(ref lots);
        if (!lots.SkipLots && TryComp<WFPlanetPoiComponent>(ground, out var pois) && pois.OutpostLots.Count > 0 && tiles.Count > 0)
        {
            var min = tiles.Aggregate(Vector2i.ComponentMin);
            var max = tiles.Aggregate(Vector2i.ComponentMax);
            var middle = (min + max) / 2;
            // An outpost with a lot of its own goes back to it; the rest take one at random.
            var kept = lots.Preferred is { } index && index >= 0 && index < pois.OutpostLots.Count ? pois.OutpostLots[index] : (Box2i?) null;
            foreach (var lot in pois.OutpostLots.OrderBy(lot => kept is { } own && lot.Equals(own) ? int.MinValue : _random.Next()))
            {
                shift = new Vector2i((lot.Left + lot.Right) / 2, (lot.Bottom + lot.Top) / 2) - middle;
                if (IsDropSiteClear(ground, tiles, shift))
                    return true;
            }
        }

        var mapId = Transform(ground).MapID;
        for (var attempt = 0; attempt < DropAttempts; attempt++)
        {
            var target = _maps.TileIndicesFor(ground, ground.Comp2, new MapCoordinates(centre + _random.NextVector2(radius), mapId));
            shift = target - consoleTile;
            if (IsDropSiteClear(ground, tiles, shift))
                return true;
        }

        shift = default;
        return false;
    }

    private bool IsDropSiteClear(Entity<BiomeComponent, MapGridComponent> ground, List<Vector2i> tiles, Vector2i shift)
    {
        var chunks = new HashSet<Vector2i>();
        foreach (var tile in tiles)
        {
            chunks.Add(SharedMapSystem.GetChunkIndices(tile + shift, BiomeChunkSize) * BiomeChunkSize);
        }

        foreach (var chunk in chunks)
        {
            _biome.WfLoadChunk(ground, chunk, hold: false);
        }

        var min = new Vector2i(int.MaxValue, int.MaxValue);
        var max = new Vector2i(int.MinValue, int.MinValue);
        foreach (var tile in tiles)
        {
            var index = tile + shift;
            if (TryGetClaim(ground, index, null, out _) || !IsOpenGround((ground, ground.Comp2), index, null, out _))
                return false;

            min = Vector2i.ComponentMin(min, index);
            max = Vector2i.ComponentMax(max, index);
        }

        return tiles.Count > 0 && !IsNearReserved(ground, min, max);
    }

    /// <summary>Takes a drop off the queue and answers its caller; a drop no longer queued is ignored.</summary>
    private void FinishDrop(PendingDrop drop, EntityUid? outpost, string reason)
    {
        if (!_drops.Remove(drop))
            return;

        _busy.Remove(drop.Owner);
        drop.Done(outpost, reason);
    }

    private void ClearDrops()
    {
        foreach (var drop in _drops)
        {
            _busy.Remove(drop.Owner);
        }

        _drops.Clear();
        _nextDrop = TimeSpan.Zero;
    }
}
