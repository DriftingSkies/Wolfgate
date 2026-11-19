using System.Linq;
using Content.Server._WG.Farming;
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

    /// <summary>Bait loaded on the hook, and how good it is (quality steps it may add).</summary>
    [DataField]
    public int Bait;

    [DataField]
    public int BaitQuality;

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

    [ViewVariables]
    public bool Baited;
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

/// <summary>
/// Fishing. A rod used on open water (a planet's lakes and ponds, by its biome) or on a built pond casts the line. After
/// a wait the fish bites and the angler has a moment to reel in, by using the rod; too early scares it off, too late
/// lets it go. What bites follows the world's temperature (<c>wgFishTable</c>), and the catch rolls a quality, better
/// with bait. Walking off snaps the line.
/// </summary>
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

    private static readonly SoundSpecifier Splash = new SoundPathSpecifier("/Audio/Effects/Fluids/splash.ogg");
    private static readonly SoundSpecifier BigSplash = new SoundPathSpecifier("/Audio/Effects/Fluids/watersplash.ogg");

    /// <summary>Seconds a fish holds the hook before it slips off.</summary>
    private const float Window = 2.5f;

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
        rod.Comp.Baited = rod.Comp.Bait > 0;
        if (rod.Comp.Baited)
            rod.Comp.Bait--;
        var wait = _random.NextFloat(8f, 22f) * (rod.Comp.Baited ? 0.6f : 1f);
        rod.Comp.BiteAt = _timing.CurTime + TimeSpan.FromSeconds(wait);
        _audio.PlayPvs(Splash, args.ClickLocation);
        _popup.PopupEntity(Loc.GetString("wg-fish-cast"), args.User, args.User);
    }

    /// <summary>Whether the spot is a planet's standing water by its biome.</summary>
    private bool IsOpenWater(EntityCoordinates where)
    {
        var map = _transform.GetMap(where);
        if (map == null || !TryComp<BiomeComponent>(map, out var biome) || !TryComp<MapGridComponent>(map, out var grid))
            return false;

        var indices = _maps.TileIndicesFor(map.Value, grid, where);
        return _biomes.WgIsWater((map.Value, biome), indices);
    }

    private string? PickTable(EntityUid user, bool pond)
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
        {
            var fish = Spawn(Pick(table.Fish), Transform(user).Coordinates);
            var quality = Roll(rod.Comp.Baited ? rod.Comp.BaitQuality : 0);
            if (quality != WGCropQuality.Normal)
                EnsureComp<WGProduceQualityComponent>(fish).Quality = quality;
            _hands.PickupOrDrop(user, fish);
            _audio.PlayPvs(BigSplash, user);
            _popup.PopupEntity(Loc.GetString("wg-fish-caught", ("fish", fish)), user, user, PopupType.Medium);
        }

        Reset(rod);
    }

    private EntProtoId Pick(List<WGFishEntry> fish)
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
    private WGCropQuality Roll(int bait)
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

            if (TerminatingOrDeleted(angler) || !_transform.InRange(Transform(angler).Coordinates, rod.CastFrom, 1.5f))
            {
                if (!TerminatingOrDeleted(angler))
                    _popup.PopupEntity(Loc.GetString("wg-fish-line-snaps"), angler, angler);
                Reset((uid, rod));
                continue;
            }

            if (!rod.Biting && now >= rod.BiteAt)
            {
                rod.Biting = true;
                rod.EscapeAt = now + TimeSpan.FromSeconds(Window);
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
        if (rod.Comp.Bait >= 10)
        {
            _popup.PopupEntity(Loc.GetString("wg-fish-bait-full"), rod, args.User);
            return;
        }

        rod.Comp.Bait++;
        rod.Comp.BaitQuality = bait.Quality;
        if (TryComp<StackComponent>(args.Used, out var stack) && stack.Count > 1)
            EntityManager.System<SharedStackSystem>().SetCount(args.Used, stack.Count - 1, stack);
        else
            QueueDel(args.Used);
        _popup.PopupEntity(Loc.GetString("wg-fish-baited", ("count", rod.Comp.Bait)), rod, args.User);
    }

    private void OnExamined(Entity<WGFishingRodComponent> rod, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-fish-rod-examine", ("bait", rod.Comp.Bait)));
    }
}
