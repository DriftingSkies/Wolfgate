using System.Linq;
using Content.Server._WG.Farming;
using Content.Server._WG.GroundCover;
using Content.Server._WG.Planets;
using Content.Server.Parallax;
using Content.Server.Popups;
using Content.Shared._WG.Farming;
using Content.Shared._WG.Fishing;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Fishing;

/// <summary>A fishing rod: cast it at water, wait for a bite, and reel in before the fish gets away.</summary>
[RegisterComponent, Access(typeof(WGFishingSystem))]
public sealed partial class WGFishingRodComponent : Component
{
    /// <summary>How far it casts, in tiles.</summary>
    [DataField]
    public float Range = 5f;

    /// <summary>Bait loaded on the hook, each its own quality (steps it may add); the last loaded goes on first.</summary>
    [DataField]
    public List<int> Bait = new();

    /// <summary>Quality of the bait on this cast's hook; null for a bare hook.</summary>
    [ViewVariables]
    public int? CastBait;

    [ViewVariables]
    public EntityUid? Angler;

    [ViewVariables]
    public EntityCoordinates CastFrom;

    [ViewVariables]
    public string? Table;

    [ViewVariables]
    public TimeSpan BiteAt;

    [ViewVariables]
    public TimeSpan EscapeAt;

    [ViewVariables]
    public bool Biting;

    /// <summary>Seconds added to the window a fish holds the hook.</summary>
    [DataField]
    public float WindowBonus;

    /// <summary>Scales the wait for a bite; lower is quicker.</summary>
    [DataField]
    public float BiteSpeed = 1f;

    /// <summary>Steps of bait quality the rod adds to every catch.</summary>
    [DataField]
    public int QualityBonus;

    /// <summary>Scales the chance of a legendary fish or a treasure chest.</summary>
    [DataField]
    public float Luck = 1f;
}

/// <summary>Bait for a fishing rod: used on a rod it loads the hook; better bait means quicker bites and better fish.</summary>
[RegisterComponent]
public sealed partial class WGBaitComponent : Component
{
    [DataField]
    public int Quality = 1;
}

/// <summary>Water that can be fished where the world itself has none: a built fish pond.</summary>
[RegisterComponent]
public sealed partial class WGFishingSpotComponent : Component;

/// <summary>Fishing.</summary>
public sealed class WGFishingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGPlanetClimateSystem _climate = default!;
    [Dependency] private WGGroundCoverSystem _groundCover = default!;

    private static readonly SoundSpecifier Splash = new SoundPathSpecifier("/Audio/Effects/Fluids/splash.ogg");
    private static readonly SoundSpecifier BigSplash = new SoundPathSpecifier("/Audio/Effects/Fluids/watersplash.ogg");

    /// <summary>Seconds a fish holds the hook before it slips off.</summary>
    private const float Window = 2.5f;

    private static readonly ProtoId<WGFishLootPrototype> LootId = "WGFishLoot";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGFishingRodComponent, AfterInteractEvent>(OnCast);
        SubscribeLocalEvent<WGFishingRodComponent, UseInHandEvent>(OnReel);
        SubscribeLocalEvent<WGFishingRodComponent, InteractUsingEvent>(OnBait);
        SubscribeLocalEvent<WGFishingRodComponent, ExaminedEvent>(OnExamined);
    }

    private void OnCast(Entity<WGFishingRodComponent> rod, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (rod.Comp.Angler != null)
        {
            args.Handled = true;
            Reel(rod, args.User);
            return;
        }

        var user = Transform(args.User).Coordinates;
        if (!_transform.InRange(user, args.ClickLocation, rod.Comp.Range))
            return;

        string? table;
        if (args.Target is { } target && HasComp<WGFishingSpotComponent>(target))
            table = PickTable(args.User, pond: true);
        else if (IsOpenWater(args.ClickLocation))
            table = PickTable(args.User, pond: false);
        else
            return;

        args.Handled = true;
        if (table == null)
        {
            _popup.PopupEntity(Loc.GetString("wg-fish-nothing-here"), args.User, args.User);
            return;
        }

        rod.Comp.Angler = args.User;
        rod.Comp.CastFrom = user;
        rod.Comp.Table = table;
        rod.Comp.Biting = false;
        rod.Comp.CastBait = null;
        if (rod.Comp.Bait.Count > 0)
        {
            rod.Comp.CastBait = rod.Comp.Bait[^1];
            rod.Comp.Bait.RemoveAt(rod.Comp.Bait.Count - 1);
        }
        var wait = _random.NextFloat(8f, 22f) * (rod.Comp.CastBait != null ? 0.6f : 1f) * rod.Comp.BiteSpeed;
        rod.Comp.BiteAt = _timing.CurTime + TimeSpan.FromSeconds(wait);
        _audio.PlayPvs(Splash, args.ClickLocation);
        _popup.PopupEntity(Loc.GetString("wg-fish-cast"), args.User, args.User);
    }

    /// <summary>Whether the spot is a planet's standing water by its biome.</summary>
    public bool IsOpenWater(EntityCoordinates where)
    {
        var map = _transform.GetMap(where);
        if (map == null || !TryComp<BiomeComponent>(map, out var biome) || !TryComp<MapGridComponent>(map, out var grid))
            return false;

        var indices = _maps.TileIndicesFor(map.Value, grid, where);
        return _biomes.WgIsWater((map.Value, biome), indices);
    }

    public string? PickTable(EntityUid user, bool pond)
    {
        var celsius = Transform(user).MapUid is { } map && _climate.GetOutsideTemperature(map) is { } kelvin ? kelvin - 273.15f : 18f;
        return _proto.EnumeratePrototypes<WGFishTablePrototype>()
            .Where(t => t.Pond == pond && (pond || celsius >= t.MinTemp && celsius <= t.MaxTemp))
            .OrderBy(t => t.ID)
            .FirstOrDefault()?.ID;
    }

    private void OnReel(Entity<WGFishingRodComponent> rod, ref UseInHandEvent args)
    {
        if (args.Handled || rod.Comp.Angler == null)
            return;

        args.Handled = true;
        Reel(rod, args.User);
    }

    private void Reel(Entity<WGFishingRodComponent> rod, EntityUid user)
    {
        if (!rod.Comp.Biting)
        {
            _popup.PopupEntity(Loc.GetString("wg-fish-too-early"), user, user);
            Reset(rod);
            return;
        }

        if (rod.Comp.Table is { } tableId && _proto.TryIndex<WGFishTablePrototype>(tableId, out var table) && table.Fish.Count > 0)
            Land(rod, user, table);

        Reset(rod);
    }

    /// <summary>Lands the catch: mostly a fish of the water, now and then junk or its legendary fish, rarely a chest besides.</summary>
    private void Land(Entity<WGFishingRodComponent> rod, EntityUid user, WGFishTablePrototype table)
    {
        var bait = rod.Comp.CastBait ?? 0;
        var at = Transform(user).Coordinates;
        _proto.TryIndex(LootId, out var loot);
        var extras = Extras(table.ID);

        EntityUid caught;
        var legendary = false;
        if (loot != null && loot.Junk.Count > 0 && _random.Prob(loot.JunkChance / (1f + bait)))
        {
            caught = Spawn(Pick(loot.Junk), at);
            _popup.PopupEntity(Loc.GetString("wg-fish-caught-junk", ("item", caught)), user, user);
        }
        else
        {
            legendary = loot != null && extras?.Legendary != null
                && _random.Prob(loot.LegendaryChance * (1f + 0.5f * bait) * rod.Comp.Luck);
            caught = Spawn(legendary ? extras!.Legendary!.Value : Pick(FishFor(user, table, extras)), at);
            var quality = legendary ? WGCropQuality.Normal : Roll(bait + rod.Comp.QualityBonus);
            if (quality != WGCropQuality.Normal)
                EnsureComp<WGProduceQualityComponent>(caught).Quality = quality;
            _popup.PopupEntity(Loc.GetString(legendary ? "wg-fish-caught-legendary" : "wg-fish-caught", ("fish", caught)),
                user, user, legendary ? PopupType.LargeCaution : PopupType.Medium);
        }

        _hands.PickupOrDrop(user, caught);
        _audio.PlayPvs(BigSplash, user);

        if (loot?.Treasure is { } treasure && _random.Prob(loot.TreasureChance * rod.Comp.Luck))
        {
            var chest = Spawn(treasure, at);
            _hands.PickupOrDrop(user, chest);
            _popup.PopupEntity(Loc.GetString("wg-fish-caught-treasure"), user, user, PopupType.Large);
        }
    }

    /// <summary>The extras prototype for a fish table, if any.</summary>
    public WGFishExtrasPrototype? Extras(string table)
    {
        return _proto.EnumeratePrototypes<WGFishExtrasPrototype>().FirstOrDefault(e => e.Table == table);
    }

    /// <summary>A table's fish, with its night fish after dark and its rain fish in the rain.</summary>
    public List<WGFishEntry> FishFor(EntityUid user, WGFishTablePrototype table, WGFishExtrasPrototype? extras)
    {
        if (extras == null || table.Pond || Transform(user).MapUid is not { } map)
            return table.Fish;

        var fish = new List<WGFishEntry>(table.Fish);
        if (extras.Night.Count > 0 && _climate.IsNight(map))
            fish.AddRange(extras.Night);
        if (extras.Rain.Count > 0 && _groundCover.GetPrecipitation(map).Rain > 0.1f)
            fish.AddRange(extras.Rain);
        return fish;
    }

    public EntProtoId Pick(List<WGFishEntry> fish)
    {
        var roll = _random.NextFloat() * fish.Sum(f => f.Weight);
        foreach (var entry in fish)
        {
            roll -= entry.Weight;
            if (roll <= 0)
                return entry.Id;
        }

        return fish[^1].Id;
    }

    /// <summary>A catch's quality: mostly normal; bait gives each step a better chance.</summary>
    public WGCropQuality Roll(int bait)
    {
        var quality = WGCropQuality.Normal;
        var chance = 0.25f + 0.15f * bait;
        while (quality < WGCropQuality.Iridium && _random.Prob(chance))
        {
            quality++;
            chance *= 0.5f;
        }

        return quality;
    }

    private void Reset(Entity<WGFishingRodComponent> rod)
    {
        rod.Comp.Angler = null;
        rod.Comp.Biting = false;
        rod.Comp.Table = null;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGFishingRodComponent>();
        while (query.MoveNext(out var uid, out var rod))
        {
            if (rod.Angler is not { } angler)
                continue;

            // The line snaps if the angler walks off or lets go of the rod, so nobody else reels in their catch.
            if (TerminatingOrDeleted(angler) || !_hands.IsHolding(angler, uid)
                || !_transform.InRange(Transform(angler).Coordinates, rod.CastFrom, 1.5f))
            {
                if (!TerminatingOrDeleted(angler))
                    _popup.PopupEntity(Loc.GetString("wg-fish-line-snaps"), angler, angler);
                Reset((uid, rod));
                continue;
            }

            if (!rod.Biting && now >= rod.BiteAt)
            {
                rod.Biting = true;
                rod.EscapeAt = now + TimeSpan.FromSeconds(Window + rod.WindowBonus);
                _audio.PlayPvs(Splash, angler);
                _popup.PopupEntity(Loc.GetString("wg-fish-bite"), angler, angler, PopupType.LargeCaution);
            }
            else if (rod.Biting && now >= rod.EscapeAt)
            {
                _popup.PopupEntity(Loc.GetString("wg-fish-got-away"), angler, angler);
                Reset((uid, rod));
            }
        }
    }

    private void OnBait(Entity<WGFishingRodComponent> rod, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<WGBaitComponent>(args.Used, out var bait))
            return;

        args.Handled = true;
        if (rod.Comp.Bait.Count >= 10)
        {
            _popup.PopupEntity(Loc.GetString("wg-fish-bait-full"), rod, args.User);
            return;
        }

        rod.Comp.Bait.Add(bait.Quality);
        if (TryComp<StackComponent>(args.Used, out var stack) && stack.Count > 1)
            EntityManager.System<SharedStackSystem>().SetCount(args.Used, stack.Count - 1, stack);
        else
            QueueDel(args.Used);
        _popup.PopupEntity(Loc.GetString("wg-fish-baited", ("count", rod.Comp.Bait.Count)), rod, args.User);
    }

    private void OnExamined(Entity<WGFishingRodComponent> rod, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-fish-rod-examine", ("bait", rod.Comp.Bait.Count)));
    }
}
