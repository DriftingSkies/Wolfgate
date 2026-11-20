using Content.Server._WG.Farming;
using Content.Server.Animals.Components;
using Content.Server.Animals.Systems;
using Content.Server.Popups;
using Content.Shared._WG.Farming;
using Content.Shared.Animals;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Farming;

/// <summary>A farm animal's mood, after Stardew: petting and feeding raise it, hunger lowers it.</summary>
[RegisterComponent, Access(typeof(WGAnimalMoodSystem))]
public sealed partial class WGAnimalMoodComponent : Component
{
    [DataField]
    public float Mood = 50f;

    /// <summary>When it was last petted; once every <see cref="WGAnimalMoodSystem.PetEvery"/> counts.</summary>
    [DataField]
    public TimeSpan LastPet;
}

/// <summary>Mood for animals that lay eggs, give milk or grow wool.</summary>
public sealed class WGAnimalMoodSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private EggLayerSystem _eggs = default!;
    [Dependency] private UdderSystem _udders = default!;
    [Dependency] private WoolySystem _wool = default!;

    public static readonly TimeSpan PetEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    private TimeSpan _next;
    private readonly HashSet<EntityUid> _animals = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGAnimalMoodComponent, InteractHandEvent>(OnPet);
        SubscribeLocalEvent<WGAnimalMoodComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGEggLaidEvent>(OnEggLaid);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + Tick;

        // Only the animals that make something; walking every entity here stalled the server.
        _animals.Clear();
        var eggs = EntityQueryEnumerator<EggLayerComponent>();
        while (eggs.MoveNext(out var uid, out _))
            _animals.Add(uid);
        var udders = EntityQueryEnumerator<UdderComponent>();
        while (udders.MoveNext(out var uid, out _))
            _animals.Add(uid);
        var wools = EntityQueryEnumerator<WoolyComponent>();
        while (wools.MoveNext(out var uid, out _))
            _animals.Add(uid);

        foreach (var uid in _animals)
        {
            var mood = EnsureComp<WGAnimalMoodComponent>(uid);
            var hungry = TryComp<HungerComponent>(uid, out var hunger) && hunger.CurrentThreshold <= HungerThreshold.Peckish;
            mood.Mood = Math.Clamp(mood.Mood + (hungry ? -4f : 1f), 0f, 100f);

            // Happy animals bring their next product forward; miserable ones push it back, at half the pace. A played
            // animal makes nothing (upstream skips it), so nothing builds up to come out at once when the player leaves.
            var shift = mood.Mood >= 70f ? -Tick * 0.5 : mood.Mood < 30f ? Tick * 0.5 : TimeSpan.Zero;
            if (shift == TimeSpan.Zero || HasComp<ActorComponent>(uid))
                continue;
            if (TryComp<EggLayerComponent>(uid, out var egg))
                _eggs.WgSetNextGrowth(egg, Shifted(egg.NextGrowth, shift));
            if (TryComp<UdderComponent>(uid, out var udder))
                _udders.WgSetNextGrowth(udder, Shifted(udder.NextGrowth, shift));
            if (TryComp<WoolyComponent>(uid, out var wool))
                _wool.WgSetNextGrowth(wool, Shifted(wool.NextGrowth, shift));
        }
    }

    /// <summary>A product's next time moved by a shift, but never brought forward past now.</summary>
    private TimeSpan Shifted(TimeSpan next, TimeSpan shift)
    {
        var now = _timing.CurTime;
        if (shift > TimeSpan.Zero || next <= now)
            return shift > TimeSpan.Zero ? next + shift : next;
        var moved = next + shift;
        return moved < now ? now : moved;
    }

    private void OnPet(Entity<WGAnimalMoodComponent> ent, ref InteractHandEvent args)
    {
        if (_timing.CurTime - ent.Comp.LastPet < PetEvery)
            return;

        ent.Comp.LastPet = _timing.CurTime;
        ent.Comp.Mood = Math.Min(100f, ent.Comp.Mood + 15f);
        _popup.PopupEntity(Loc.GetString("wg-mood-petted", ("animal", ent.Owner)), ent, args.User);
    }

    private void OnExamined(Entity<WGAnimalMoodComponent> ent, ref ExaminedEvent args)
    {
        var key = ent.Comp.Mood >= 70f ? "happy" : ent.Comp.Mood >= 30f ? "content" : "miserable";
        args.PushMarkup(Loc.GetString("wg-mood-" + key, ("animal", ent.Owner)));
    }

    private void OnEggLaid(ref WGEggLaidEvent args)
    {
        if (!TryComp<WGAnimalMoodComponent>(args.Layer, out var mood) || mood.Mood < 70f)
            return;

        var quality = mood.Mood >= 95f && _random.Prob(0.3f) ? WGCropQuality.Gold
            : _random.Prob((mood.Mood - 60f) / 50f) ? WGCropQuality.Silver
            : WGCropQuality.Normal;
        if (quality == WGCropQuality.Normal)
            return;

        foreach (var egg in args.Eggs)
            EnsureComp<WGProduceQualityComponent>(egg).Quality = quality;
    }
}

/// <summary>Raised broadcast when an animal lays, with what it laid.</summary>
[ByRefEvent]
public readonly record struct WGEggLaidEvent(EntityUid Layer, List<EntityUid> Eggs);
