using Content.Server._WG.Remade.Corruption;
using Content.Server._WG.Remade.Monolith;
using Content.Server._WG.Remade.Structures;
using Content.Server.Cargo.Components;
using Content.Server.Kitchen.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WG.Remade;
using Content.Shared.Examine;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Pulling.Events;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Gifts;

/// <summary>
/// Shards. A sharp tool or a welder on an anchored Scion chips one off and adds resonance. A shard is a power cell that never runs
/// down. It sells high while its Scion is quiet. Once its Scion reaches Growth, a shard left still for two minutes roots and grows a
/// little corruption, and needs a sharp tool to cut free. A shard goes when its Scion does.
/// </summary>
public sealed partial class WGShardSystem : EntitySystem
{
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCorruptionSystem _corruption = default!;
    [Dependency] private WGMonolithSystem _monolith = default!;

    public static readonly EntProtoId ShardProto = "WGMonolithShard";

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly SoundSpecifier ChipSound = new SoundCollectionSpecifier("MetalThud");
    private const float ChipSeconds = 8f;
    private const float WelderFuel = 5f;
    private const float MoveTolerance = 0.25f;

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMonolithComponent, InteractUsingEvent>(OnChipUse);
        SubscribeLocalEvent<WGMonolithComponent, WGMonolithChipDoAfterEvent>(OnChipped);
        SubscribeLocalEvent<WGMonolithShardComponent, InteractUsingEvent>(OnCutUse);
        SubscribeLocalEvent<WGMonolithShardComponent, WGMonolithShardCutDoAfterEvent>(OnCut);
        SubscribeLocalEvent<WGMonolithShardComponent, GettingPickedUpAttemptEvent>(OnPickup);
        SubscribeLocalEvent<WGMonolithShardComponent, BeingPulledAttemptEvent>(OnPulled);
        SubscribeLocalEvent<WGMonolithShardComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGMonolithShardComponent, EntityTerminatingEvent>(OnTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickRate;
        var shards = EntityQueryEnumerator<WGMonolithShardComponent>();
        while (shards.MoveNext(out var uid, out var shard))
        {
            Tick((uid, shard), now);
        }
    }

    private void Tick(Entity<WGMonolithShardComponent> shard, TimeSpan now)
    {
        if (shard.Comp.Parent is not { } parent || TerminatingOrDeleted(parent) || !TryComp<WGMonolithComponent>(parent, out var monolith))
        {
            QueueDel(shard);
            return;
        }

        var stage = monolith.Stage;
        _appearance.SetData(shard, WGMonolithVisuals.Stage, stage);

        // The same refusal as the Scion: a buyer only takes one while it is quiet.
        if (stage == WGMonolithStage.Quiet)
            RemComp<CargoSellBlacklistComponent>(shard);
        else
            EnsureComp<CargoSellBlacklistComponent>(shard);

        // The cell works anywhere but on or near a main station.
        var barred = _corruption.IsSanctuaryAt(shard);
        if (TryComp<BatterySelfRechargerComponent>(shard, out var recharger))
            recharger.AutoRecharge = !barred;

        if (!barred && TryComp<BatteryComponent>(shard, out var battery))
            _battery.SetCharge(shard, battery.MaxCharge, battery);

        TrackStill((shard, shard.Comp), (parent, monolith), now);
    }

    /// <summary>Roots a shard that has lain still long enough, and uproots one that has been moved.</summary>
    private void TrackStill(Entity<WGMonolithShardComponent> shard, Entity<WGMonolithComponent> parent, TimeSpan now)
    {
        var comp = shard.Comp;
        var xform = Transform(shard);
        if (_container.IsEntityInContainer(shard) || !_transform.InRange(comp.Last, xform.Coordinates, MoveTolerance))
        {
            if (comp.Deployed)
                Undeploy(shard);

            comp.StillSince = now;
            comp.Last = xform.Coordinates;
            return;
        }

        if (comp.Deployed || parent.Comp.Stage < WGMonolithStage.Growth || now - comp.StillSince < comp.DeployAfter)
            return;

        Deploy(shard, parent);
    }

    private void Deploy(Entity<WGMonolithShardComponent> shard, Entity<WGMonolithComponent> parent)
    {
        if (!TryComp<WGCorruptionNodeComponent>(shard, out var node) || !_corruption.SeedShard(shard, (shard, node), parent))
            return;

        shard.Comp.Deployed = true;
        _popup.PopupEntity(Loc.GetString("wg-shard-rooted"), shard, PopupType.MediumCaution);
    }

    /// <summary>Plants a shard of a woken stone already rooted where it lands. Null if nothing can root there.</summary>
    public EntityUid? PlantRooted(Entity<WGMonolithComponent> parent, EntityCoordinates at)
    {
        var shard = Spawn(ShardProto, at);
        var comp = Comp<WGMonolithShardComponent>(shard);
        comp.Parent = parent;
        comp.Last = Transform(shard).Coordinates;
        comp.StillSince = _timing.CurTime;
        parent.Comp.Shards.Add(shard);
        Deploy((shard, comp), parent);
        if (comp.Deployed)
            return shard;

        parent.Comp.Shards.Remove(shard);
        Del(shard);
        return null;
    }

    private void Undeploy(Entity<WGMonolithShardComponent> shard)
    {
        shard.Comp.Deployed = false;
        if (TryComp<WGCorruptionNodeComponent>(shard, out var node))
            _corruption.DetachNode((shard, node));
    }

    private void OnChipUse(Entity<WGMonolithComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var tool = args.Used;
        var welder = _tool.HasQuality(tool, "Welding");
        if (!welder && !HasComp<SharpComponent>(tool))
            return;

        args.Handled = true;
        if (!Transform(ent).Anchored)
        {
            _popup.PopupEntity(Loc.GetString("wg-shard-chip-loose"), ent, args.User);
            return;
        }

        if (!CanChip(ent, args.User))
            return;

        _popup.PopupEntity(Loc.GetString("wg-shard-chip-start", ("tool", tool)), ent, args.User);
        if (welder)
        {
            _tool.UseTool(tool, args.User, ent, ChipSeconds * 1.5f, "Welding", new WGMonolithChipDoAfterEvent(), WelderFuel);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ChipSeconds, new WGMonolithChipDoAfterEvent(), ent, ent, tool)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private bool CanChip(Entity<WGMonolithComponent> ent, EntityUid user)
    {
        if (_monolith.IsBarred(ent))
        {
            _popup.PopupEntity(Loc.GetString("wg-shard-chip-barred"), ent, user);
            return false;
        }

        if (ent.Comp.Shards.Count >= WGMonolithMath.MaxShards)
        {
            _popup.PopupEntity(Loc.GetString("wg-shard-chip-none-left"), ent, user);
            return false;
        }

        return true;
    }

    private void OnChipped(Entity<WGMonolithComponent> ent, ref WGMonolithChipDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (!Transform(ent).Anchored || !CanChip(ent, args.User))
            return;

        var shard = Spawn(ShardProto, Transform(ent).Coordinates);
        Comp<WGMonolithShardComponent>(shard).Parent = ent;
        ent.Comp.Shards.Add(shard);
        _monolith.AddResonance(ent, WGMonolithMath.ChipResonance);
        _audio.PlayPvs(ChipSound, ent);
        _popup.PopupEntity(Loc.GetString("wg-shard-chipped"), ent, args.User, PopupType.MediumCaution);
    }

    private void OnCutUse(Entity<WGMonolithShardComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Deployed || !HasComp<SharpComponent>(args.Used))
            return;

        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.CutSeconds, new WGMonolithShardCutDoAfterEvent(), ent, ent, args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnCut(Entity<WGMonolithShardComponent> ent, ref WGMonolithShardCutDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ent.Comp.Deployed)
            return;

        args.Handled = true;
        Undeploy(ent);
        ent.Comp.StillSince = _timing.CurTime;
        _popup.PopupEntity(Loc.GetString("wg-shard-cut-free"), ent, args.User);
    }

    private void OnPickup(Entity<WGMonolithShardComponent> ent, ref GettingPickedUpAttemptEvent args)
    {
        if (!ent.Comp.Deployed)
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wg-shard-stuck"), ent, args.User);
    }

    private void OnPulled(Entity<WGMonolithShardComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (ent.Comp.Deployed)
            args.Cancel();
    }

    private void OnExamined(Entity<WGMonolithShardComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Deployed ? "wg-shard-examine-rooted" : "wg-shard-examine"));
    }

    private void OnTerminating(Entity<WGMonolithShardComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<WGMonolithComponent>(ent.Comp.Parent, out var monolith))
            monolith.Shards.Remove(ent);
    }
}
