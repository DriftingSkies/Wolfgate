using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Administration.Logs;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Interaction.Events;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// Outpost modules: the owner buys a module's next level at the outpost console, waits out its build, and the outpost
/// gains what the level gives. A level with a prefab hands out a survey stake that lays it onto the outpost.
/// </summary>
public sealed partial class WFHideoutSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;

    private static readonly EntProtoId StakePrototype = "WFHideoutSurveyStake";

    private static readonly TimeSpan BuildInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextBuild;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOutpostComponent, ComponentStartup>(OnOutpostStartup);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostConsoleStateEvent>(OnConsoleState);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostCapsEvent>(OnCaps);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostPodLimitEvent>(OnPodLimit);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostFingerprintEvent>(OnFingerprint);
        SubscribeLocalEvent<WFHideoutComponent, WFOutpostSettledEvent>(OnSettled);
        SubscribeLocalEvent<WFHideoutStakeComponent, AfterInteractEvent>(OnStakeAfterInteract);
        SubscribeLocalEvent<WFHideoutStakeComponent, UseInHandEvent>(OnStakeUseInHand);
        Subs.BuiEvents<WFOutpostConsoleComponent>(WFOutpostConsoleUiKey.Key, subs =>
        {
            subs.Event<WFHideoutBuildMessage>(OnBuildMessage);
            subs.Event<WFHideoutRegisterShipMessage>(OnRegisterMessage);
            subs.Event<WFHideoutUnregisterShipMessage>(OnUnregisterMessage);
            subs.Event<WFHideoutRecallShipMessage>(OnRecallMessage);
            subs.Event<WFHideoutInsureMessage>(OnInsureMessage);
            subs.Event<WFHideoutClaimMessage>(OnClaimMessage);
            subs.Event<WFHideoutMoveHomeMessage>(OnMoveHomeMessage);
        });
        InitializeHangar();
        InitializeHome();
    }

    private void OnOutpostStartup(Entity<WFOutpostComponent> outpost, ref ComponentStartup args)
    {
        EnsureComp<WFHideoutComponent>(outpost);
    }

    /// <summary>The level a module stands at on an outpost, 0 when it isn't built.</summary>
    public int Level(EntityUid outpost, ProtoId<WFHideoutModulePrototype> module)
    {
        return TryComp<WFHideoutComponent>(outpost, out var hideout) ? hideout.Levels.GetValueOrDefault(module) : 0;
    }

    /// <summary>Every level built on an outpost, across its modules.</summary>
    private IEnumerable<WFHideoutModuleLevel> Built(WFHideoutComponent hideout)
    {
        foreach (var (id, level) in hideout.Levels)
        {
            if (!_proto.TryIndex<WFHideoutModulePrototype>(id, out var module))
                continue;

            for (var i = 0; i < level && i < module.Levels.Count; i++)
            {
                yield return module.Levels[i];
            }
        }
    }

    /// <summary>
    /// Starts the next level of a module for the outpost's owner, paid from their bank. <paramref name="reason"/> is a
    /// loc id when it fails.
    /// </summary>
    public bool TryBuild(EntityUid user, Entity<WFOutpostComponent> outpost, ProtoId<WFHideoutModulePrototype> id, out string reason)
    {
        if (!_cfg.GetCVar(HideoutCVars.Enabled) || !_outposts.IsOwnerOf(user, outpost.Comp)
            || !TryComp<WFHideoutComponent>(outpost, out var hideout))
        {
            reason = "wf-hideout-build-not-owner";
            return false;
        }

        if (hideout.Building != null)
        {
            reason = "wf-hideout-build-busy";
            return false;
        }

        if (!_proto.TryIndex(id, out var module) || hideout.Levels.GetValueOrDefault(id) >= module.Levels.Count)
        {
            reason = "wf-hideout-build-done";
            return false;
        }

        if (module.Company && !HasComp<CompanyComponent>(outpost))
        {
            reason = "wf-hideout-build-company";
            return false;
        }

        var level = module.Levels[hideout.Levels.GetValueOrDefault(id)];
        foreach (var (needed, at) in level.Requires)
        {
            if (hideout.Levels.GetValueOrDefault(needed) < at)
            {
                reason = "wf-hideout-build-requires";
                return false;
            }
        }

        if (!HasMaterials(outpost, level))
        {
            reason = "wf-hideout-build-materials";
            return false;
        }

        if (level.Price > 0 && !_bank.TryBankWithdraw(user, level.Price))
        {
            reason = "wf-hideout-cannot-afford";
            return false;
        }

        TakeMaterials(outpost, level);

        hideout.Building = id;
        hideout.BuildLeft = level.BuildTime;
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} started {id} level {hideout.Levels.GetValueOrDefault(id) + 1} on outpost {ToPrettyString(outpost):grid} for {level.Price}");
        reason = string.Empty;
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextBuild)
            return;

        _nextBuild = _timing.CurTime + BuildInterval;
        var query = EntityQueryEnumerator<WFHideoutComponent, WFOutpostComponent>();
        while (query.MoveNext(out var uid, out var hideout, out var outpost))
        {
            if (hideout.Building == null)
                continue;

            hideout.BuildLeft -= (float) BuildInterval.TotalSeconds;
            if (hideout.BuildLeft <= 0f)
                Complete((uid, hideout, outpost));
        }
    }

    /// <summary>Finishes the level being built: the module goes up a level and its grants and stake are left at the console.</summary>
    private void Complete(Entity<WFHideoutComponent, WFOutpostComponent> outpost)
    {
        var hideout = outpost.Comp1;
        if (hideout.Building is not { } id)
            return;

        hideout.Building = null;
        hideout.BuildLeft = 0f;
        if (!_proto.TryIndex<WFHideoutModulePrototype>(id, out var module))
            return;

        var index = hideout.Levels.GetValueOrDefault(id);
        if (index >= module.Levels.Count)
            return;

        hideout.Levels[id] = index + 1;
        var level = module.Levels[index];
        var console = outpost.Comp2.Console;
        if (!TerminatingOrDeleted(console))
        {
            var at = Transform(console).Coordinates;
            foreach (var grant in level.Grants)
            {
                Spawn(grant, at);
            }

            if (level.Prefab is { } prefab)
            {
                var stake = Spawn(StakePrototype, at);
                Comp<WFHideoutStakeComponent>(stake).Prefab = prefab;
                _metaData.SetEntityName(stake, Loc.GetString("wf-hideout-stake-name", ("module", Loc.GetString(module.Name))));
            }

            _popup.PopupEntity(Loc.GetString("wf-hideout-build-finished", ("module", Loc.GetString(module.Name)), ("level", index + 1)), console);
        }

        ApplyHangar(outpost);
        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"Outpost {ToPrettyString(outpost):grid} finished {id} level {index + 1}");
        _outposts.RefreshConsole((outpost, outpost.Comp2));
    }

    /// <summary>Finishes the level an outpost is building at once; for admins and tests.</summary>
    public bool SkipBuild(EntityUid outpost)
    {
        if (!TryComp<WFHideoutComponent>(outpost, out var hideout) || !TryComp<WFOutpostComponent>(outpost, out var comp)
            || hideout.Building == null)
            return false;

        Complete((outpost, hideout, comp));
        return true;
    }

    /// <summary>Sets a module's level on an outpost outright, handing out nothing; for admins and tests.</summary>
    public bool SetLevel(EntityUid outpost, ProtoId<WFHideoutModulePrototype> id, int level)
    {
        if (!TryComp<WFHideoutComponent>(outpost, out var hideout) || !TryComp<WFOutpostComponent>(outpost, out var comp)
            || !_proto.TryIndex(id, out var module) || level < 0 || level > module.Levels.Count)
            return false;

        if (level == 0)
            hideout.Levels.Remove(id);
        else
            hideout.Levels[id] = level;
        ApplyHangar(outpost);
        _outposts.RefreshConsole((outpost, comp));
        return true;
    }

    /// <summary>How long a stake's mark-out outline stays on the ground.</summary>
    private const float MarkSeconds = 10f;

    /// <summary>Used in hand, a stake outlines its module on the tile in front of its user, facing the way they face.</summary>
    private void OnStakeUseInHand(Entity<WFHideoutStakeComponent> stake, ref UseInHandEvent args)
    {
        if (args.Handled || stake.Comp.Prefab is not { } prefab)
            return;

        args.Handled = true;
        var user = args.User;
        if (!TryComp<ActorComponent>(user, out var actor) || !_outposts.TryGetOutpost(actor.PlayerSession.UserId, out var outpost))
        {
            _popup.PopupEntity(Loc.GetString("wf-hideout-stake-no-outpost"), user, user);
            return;
        }

        var rotation = _transform.GetWorldRotation(user);
        var facing = rotation.GetCardinalDir();
        var target = _transform.GetMapCoordinates(user).Offset(facing.ToVec());
        var coords = _transform.ToCoordinates(outpost.Comp.Ground, target);
        _popup.PopupEntity(_outposts.TryMarkPrefab(user, outpost, prefab, coords, facing, MarkSeconds, out var reason)
            ? Loc.GetString("wf-hideout-stake-marked", ("seconds", (int) MarkSeconds))
            : Loc.GetString(reason), user, user);
    }

    private void OnStakeAfterInteract(Entity<WFHideoutStakeComponent> stake, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = true;
        if (!TryUseStake(args.User, stake, args.ClickLocation, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
    }

    /// <summary>
    /// Lays a stake's prefab onto its user's outpost at <paramref name="target"/>, extending the way the user faces, and
    /// uses the stake up. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryUseStake(EntityUid user, Entity<WFHideoutStakeComponent> stake, EntityCoordinates target, out string reason)
    {
        if (stake.Comp.Prefab is not { } prefab || !TryComp<ActorComponent>(user, out var actor)
            || !_outposts.TryGetOutpost(actor.PlayerSession.UserId, out var outpost))
        {
            reason = "wf-hideout-stake-no-outpost";
            return false;
        }

        var facing = _transform.GetWorldRotation(user).GetCardinalDir();
        if (!_outposts.TryStampPrefab(user, outpost, prefab, target, facing, out reason))
            return false;

        QueueDel(stake);
        ApplyHangar(outpost);
        _outposts.RefreshConsole(outpost);
        return true;
    }

    private void OnCaps(Entity<WFHideoutComponent> outpost, ref WFOutpostCapsEvent args)
    {
        foreach (var level in Built(outpost.Comp))
        {
            args.Tiles += level.Tiles;
            args.Entities += level.Entities;
            args.Span += level.Span;
        }
    }

    private void OnPodLimit(Entity<WFHideoutComponent> outpost, ref WFOutpostPodLimitEvent args)
    {
        foreach (var level in Built(outpost.Comp))
        {
            args.Limit += level.Pods;
        }
    }

    private void OnFingerprint(Entity<WFHideoutComponent> outpost, ref WFOutpostFingerprintEvent args)
    {
        var hash = new HashCode();
        hash.Add(args.Hash);
        hash.Add(outpost.Comp.Building);
        foreach (var (id, level) in outpost.Comp.Levels.OrderBy(pair => pair.Key))
        {
            hash.Add(id);
            hash.Add(level);
        }

        args.Hash = hash.ToHashCode();
    }

    private void OnConsoleState(Entity<WFHideoutComponent> outpost, ref WFOutpostConsoleStateEvent args)
    {
        if (!_cfg.GetCVar(HideoutCVars.Enabled) || !TryComp<WFOutpostComponent>(outpost, out var comp))
            return;

        EnsureHome((outpost, outpost.Comp, comp));
        var balance = 0;
        if (_players.TryGetSessionById(comp.Owner, out var session))
            _bank.TryGetBalance(session, out balance);

        args.State.Hideout = new WFHideoutView(
            new Dictionary<string, int>(outpost.Comp.Levels),
            outpost.Comp.Building,
            _timing.CurTime + TimeSpan.FromSeconds(outpost.Comp.BuildLeft),
            balance,
            HasComp<CompanyComponent>(outpost),
            HangarView((outpost, outpost.Comp, comp)),
            Holdings(outpost),
            outpost.Comp.HomeWorld,
            HomeWorlds().Select(s => new WFHideoutWorldView(s.ID, WorldName(s))).ToList(),
            HideoutRules.MoveFee);
    }

    private void OnBuildMessage(Entity<WFOutpostConsoleComponent> console, ref WFHideoutBuildMessage args)
    {
        if (!TryOutpost(console, out var outpost))
            return;

        if (!TryBuild(args.Actor, outpost, args.Module, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.Actor, args.Actor);
        _outposts.RefreshConsole(outpost);
    }

    private bool TryOutpost(Entity<WFOutpostConsoleComponent> console, out Entity<WFOutpostComponent> outpost)
    {
        outpost = default;
        if (!_cfg.GetCVar(HideoutCVars.Enabled) || console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var comp))
            return false;

        outpost = (grid, comp);
        return true;
    }
}
