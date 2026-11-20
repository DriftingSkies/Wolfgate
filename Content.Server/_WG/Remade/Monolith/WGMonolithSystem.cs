using System.Linq;
using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Counterplay;
using Content.Server._WG.Remade.Dread;
using Content.Server._WG.Remade.Sector;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Chat.Systems;
using Content.Server.Power.Components;
using Content.Shared._WG.Remade;
using Content.Shared.Cargo.Components;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Tiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Monolith;

/// <summary>Runs every Monolith: climbs its resonance, pays its power gift and moves it through its stages.</summary>
public sealed partial class WGMonolithSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGDreadAlarmSystem _alarm = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCorruptionSystem _corruption = default!;
    [Dependency] private WGRemadeClearSystem _clear = default!;
    [Dependency] private Content.Server._WG.Ghost.GhostNoticeSystem _ghostNotice = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CorpseCountRate = TimeSpan.FromSeconds(5);

    /// <summary>Least time between two stage stings, whichever stones climb.</summary>
    private static readonly TimeSpan StingGap = TimeSpan.FromSeconds(45);

    /// <summary>No stage sting plays before this. Tests clear it.</summary>
    public TimeSpan NextSting { get; set; }

    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _humanoids = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMonolithComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGMonolithComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<WGMonolithComponent, PriceCalculationEvent>(OnPrice);
        SubscribeLocalEvent<WGMonolithComponent, EntParentChangedMessage>(OnParentChanged);
    }

    /// <summary>Carried onto or off a station, its sale is decided again at once, so it can't be sold off Central meanwhile.</summary>
    private void OnParentChanged(Entity<WGMonolithComponent> ent, ref EntParentChangedMessage args)
    {
        // Not while it is still being spawned: map init decides its sale first.
        if (!TerminatingOrDeleted(ent) && MetaData(ent).EntityLifeStage >= EntityLifeStage.MapInitialized)
            UpdateSale(ent);
    }

    /// <summary>A Scion fetches more the further it has climbed: up to double its base at the Crescendo.</summary>
    private void OnPrice(Entity<WGMonolithComponent> ent, ref PriceCalculationEvent args)
    {
        if (TryComp<StaticPriceComponent>(ent, out var price))
            args.Price += price.Price * Math.Clamp(ent.Comp.Resonance / 100f, 0f, 1f);
    }

    /// <summary>Only Caelestinus Central buys a Scion, so it sells only from a pallet on a main station.</summary>
    private void UpdateSale(Entity<WGMonolithComponent> ent)
    {
        var central = Transform(ent).GridUid is { } grid && _corruption.IsMainStation(grid) && !ent.Comp.Local;
        if (central)
            RemComp<CargoSellBlacklistComponent>(ent);
        else
            EnsureComp<CargoSellBlacklistComponent>(ent);
    }

    private void OnMapInit(Entity<WGMonolithComponent> ent, ref MapInitEvent args)
    {
        SetStage(ent, WGMonolithMath.StageFor(ent.Comp.Resonance));
    }

    private void OnTerminating(Entity<WGMonolithComponent> ent, ref EntityTerminatingEvent args)
    {
        _corruption.StartDecay(ent.Comp);

        // Chipped shards go with their Scion, wherever they were carried.
        foreach (var shard in ent.Comp.Shards.ToArray())
        {
            if (!TerminatingOrDeleted(shard))
                QueueDel(shard);
        }

        ent.Comp.Shards.Clear();
        _clear.LastStoneGone(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGMonolithComponent>();
        while (query.MoveNext(out var uid, out var monolith))
        {
            if (now < monolith.NextTick)
                continue;

            monolith.NextTick = now + TickRate;
            Tick((uid, monolith), now);
        }
    }

    private void Tick(Entity<WGMonolithComponent> ent, TimeSpan now)
    {
        var monolith = ent.Comp;
        if (now >= monolith.NextCorpseCount)
        {
            monolith.NextCorpseCount = now + CorpseCountRate;
            monolith.Corpses = CountCorpses(ent);
            UpdateSale(ent);
        }

        if (IsBarred(ent))
        {
            monolith.Draw = 0f;
            monolith.MatterDraw = 0f;
            monolith.FleshDraw = 0f;
            monolith.ReachDraw = 0f;
            monolith.WardDraw = 0f;
        }

        var rate = WGMonolithMath.Rate(EffectiveDraw(ent), monolith.Resonance, monolith.Corpses) + monolith.SelfClimb;
        monolith.Resonance = Math.Min(monolith.Resonance + rate * (float) TickRate.TotalSeconds, WGMonolithMath.Cap(monolith.MaxStage));

        if (TryComp<PowerSupplierComponent>(ent, out var supplier))
            supplier.MaxSupply = WGMonolithMath.Output(monolith.Draw, monolith.Resonance);

        var stage = WGMonolithMath.StageFor(monolith.Resonance);
        if (stage != monolith.Stage)
            SetStage(ent, stage);
    }

    private int CountCorpses(Entity<WGMonolithComponent> ent)
    {
        _humanoids.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.CorpseRadius, _humanoids);

        var count = 0;
        foreach (var humanoid in _humanoids)
        {
            if (_mobState.IsDead(humanoid))
                count++;
        }

        return count;
    }

    /// <summary>A Monolith can't be drawn on while it sits on or near a main station.</summary>
    public bool IsBarred(EntityUid monolith)
    {
        return _corruption.IsSanctuaryAt(monolith);
    }

    /// <summary>Sets the Power dial.</summary>
    public void SetDraw(Entity<WGMonolithComponent> ent, float draw)
    {
        SetDial(ent, WGGift.Power, draw);
    }

    /// <summary>
    /// Sets a Gift's dial. A sanctuary grid pins every dial to nothing, and a rooted Monolith holds Matter, Flesh and Reach at full.
    /// </summary>
    public void SetDial(Entity<WGMonolithComponent> ent, WGGift gift, float value)
    {
        var monolith = ent.Comp;
        if (IsBarred(ent))
            value = 0f;
        else if (gift != WGGift.Power && monolith.Stage == WGMonolithStage.Crescendo)
            return;

        value = Math.Clamp(value, 0f, 1f);
        switch (gift)
        {
            case WGGift.Power:
                monolith.Draw = value;
                if (TryComp<PowerSupplierComponent>(ent, out var supplier))
                    supplier.MaxSupply = WGMonolithMath.Output(value, monolith.Resonance);
                break;
            case WGGift.Matter:
                monolith.MatterDraw = value;
                break;
            case WGGift.Flesh:
                monolith.FleshDraw = value;
                break;
            case WGGift.Reach:
                monolith.ReachDraw = value;
                break;
            case WGGift.Ward:
                monolith.WardDraw = value;
                break;
        }
    }

    /// <summary>The dial a Gift runs at now: nothing on a sanctuary grid, full once the Monolith has rooted.</summary>
    public float GetDial(Entity<WGMonolithComponent> ent, WGGift gift)
    {
        if (IsBarred(ent))
            return 0f;

        var monolith = ent.Comp;
        var set = gift switch
        {
            WGGift.Matter => monolith.MatterDraw,
            WGGift.Flesh => monolith.FleshDraw,
            WGGift.Reach => monolith.ReachDraw,
            WGGift.Ward => monolith.WardDraw,
            _ => monolith.Draw,
        };

        return gift == WGGift.Power ? Math.Clamp(set, 0f, 1f) : WGMonolithMath.Dial(monolith.Stage, set);
    }

    /// <summary>All the dials added up, which is what drives the climb.</summary>
    public float EffectiveDraw(Entity<WGMonolithComponent> ent)
    {
        return WGMonolithMath.EffectiveDraw(
            GetDial(ent, WGGift.Power),
            GetDial(ent, WGGift.Matter),
            GetDial(ent, WGGift.Flesh),
            GetDial(ent, WGGift.Reach),
            GetDial(ent, WGGift.Ward));
    }

    /// <summary>Adds resonance outside the climb, such as a chipped shard or a jump.</summary>
    public void AddResonance(Entity<WGMonolithComponent> ent, float amount)
    {
        ent.Comp.Resonance = Math.Clamp(ent.Comp.Resonance + amount, 0f, WGMonolithMath.Cap(ent.Comp.MaxStage));
        var stage = WGMonolithMath.StageFor(ent.Comp.Resonance);
        if (stage != ent.Comp.Stage)
            SetStage(ent, stage);
    }

    /// <summary>Takes a fed corpse's relief off the resonance. Nothing quiets a Monolith past its Crescendo.</summary>
    public bool TryQuiet(Entity<WGMonolithComponent> ent)
    {
        if (ent.Comp.Stage == WGMonolithStage.Crescendo)
            return false;

        ent.Comp.Resonance = Math.Max(0f, ent.Comp.Resonance - ent.Comp.FeedRelief);
        var stage = WGMonolithMath.StageFor(ent.Comp.Resonance);
        if (stage != ent.Comp.Stage)
            SetStage(ent, stage);

        return true;
    }

    private void SetStage(Entity<WGMonolithComponent> ent, WGMonolithStage stage)
    {
        var old = ent.Comp.Stage;
        ent.Comp.Stage = stage;
        _appearance.SetData(ent, WGMonolithVisuals.Stage, stage);

        UpdateSale(ent);

        if (_light.TryGetLight(ent, out var light))
        {
            _light.SetEnabled(ent, stage != WGMonolithStage.Quiet, light);
            _light.SetRadius(ent, 2f + 2f * (int) stage, light);
        }

        if (stage > old)
        {
            var stageName = Loc.GetString($"wg-monolith-stage-{stage.ToString().ToLowerInvariant()}");
            _ghostNotice.Notify(ent, Content.Server._WG.Ghost.GhostNoticeKind.Monolith, Loc.GetString("wg-ghost-notice-scion-stage", ("stage", stageName)));
        }

        if (stage == WGMonolithStage.Crescendo && old != WGMonolithStage.Crescendo)
            Crescendo(ent);
        else if (stage > old)
            Sting(ent, stage);
    }

    /// <summary>Everyone in the round hears the stone climb, wherever they are.</summary>
    private void Sting(Entity<WGMonolithComponent> ent, WGMonolithStage stage)
    {
        var index = (int) stage - 1;
        var now = _timing.CurTime;
        if (index < 0 || index >= ent.Comp.StageSounds.Count || !ent.Comp.Local && now < NextSting)
            return;

        // A splinter's climb is heard only aboard its own grid.
        var grid = ent.Comp.Local ? Transform(ent).GridUid : null;
        if (!ent.Comp.Local)
            NextSting = now + StingGap;

        _audio.PlayGlobal(ent.Comp.StageSounds[index], Filter.Empty().AddWhere(session =>
            session.AttachedEntity is { } attached && (grid == null || Transform(attached).GridUid == grid)), false);
        StingsPlayed++;
    }

    /// <summary>Stage stings played this round. Tests read it.</summary>
    public int StingsPlayed { get; private set; }

    /// <summary>The Scion roots where it stands, and the sector is told.</summary>
    private void Crescendo(Entity<WGMonolithComponent> ent)
    {
        RemComp<PullableComponent>(ent);

        var xform = Transform(ent);
        if (xform.GridUid != null && !xform.Anchored)
            _transform.AnchorEntity((ent, xform));

        // The alarm sounds three times, back to back, so the announcement leaves the sound to the repeater.
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("wg-monolith-crescendo-announcement"),
            Loc.GetString("wg-monolith-crescendo-sender"),
            playSound: false,
            colorOverride: Color.FromHex("#EC3232"));
        _alarm.Start(ent, ent.Comp.CrescendoSound, global: true);
        _audio.PlayGlobal(ent.Comp.CrescendoSong, Filter.Broadcast(), true);

        RaiseLocalEvent(ent, new WGMonolithRootedEvent());
    }
}
