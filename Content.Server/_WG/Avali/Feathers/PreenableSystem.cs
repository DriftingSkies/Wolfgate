using Content.Server.Chat.Systems;
using Content.Server.Forensics;
using Content.Server.Body.Components;
using Content.Shared._WG.Avali.Feathers;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Verbs;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Avali.Feathers;

/// <summary>Runs Avali feather preening, shedding and regrowth.</summary>
public sealed partial class PreenableSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ForensicsSystem _forensics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PreenableComponent, GetVerbsEvent<Verb>>(AddVerb);
        SubscribeLocalEvent<PreenableComponent, PreeningEvent>(OnPreened);
        SubscribeLocalEvent<PreenableComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<PreenableComponent, ComponentInit>(OnCompInit);
    }

    private void OnCompInit(Entity<PreenableComponent> ent, ref ComponentInit args)
    {
        ent.Comp.CurrentFeathers = ent.Comp.MaximumFeathers;
        Dirty(ent);
    }

    /// <summary>Adds the preening verb when the user can reach an Avali with feathers.</summary>
    private void AddVerb(Entity<PreenableComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !args.CanAccess || ent.Comp.CurrentFeathers <= 0)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Act = () => AttemptDoAfter(ent, user),
            Text = Loc.GetString(ent.Comp.PreeningVerbString),
        });
    }

    /// <summary>Starts the preening do-after and notifies nearby players.</summary>
    private void AttemptDoAfter(Entity<PreenableComponent> ent, EntityUid user)
    {
        var doArgs = new DoAfterArgs(EntityManager, user, 5f, new PreeningEvent(), ent, ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        };

        if (user == ent.Owner)
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.SelfPreeningMessage), ent, ent);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.GettingPreenedMessage,
                ("preener", Identity.Entity(user, EntityManager))), ent, ent, PopupType.Medium);
            _popup.PopupEntity(Loc.GetString(ent.Comp.PreeningOtherMessage,
                ("preenee", Identity.Entity(ent, EntityManager))), user, user);
        }

        _doAfter.TryStartDoAfter(doArgs);
    }

    /// <summary>Spawns a feather when the preening do-after succeeds.</summary>
    private void OnPreened(Entity<PreenableComponent> ent, ref PreeningEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.CurrentFeathers <= 0)
            return;

        args.Handled = true;
        var feather = SpawnFeather(ent, bloody: false);
        _hands.TryPickupAnyHand(args.User, feather);
    }

    /// <summary>Sheds a feather after a sufficiently damaging brute hit.</summary>
    private void OnDamaged(Entity<PreenableComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageDelta == null || ent.Comp.ValidDamageGroups == null || !args.DamageIncreased ||
            ent.Comp.CurrentFeathers <= 0)
            return;

        var totalApplicableDamage = FixedPoint2.Zero;
        foreach (var (group, value) in args.DamageDelta.GetDamagePerGroup(_prototype))
        {
            if (ent.Comp.ValidDamageGroups.Contains(group))
                totalApplicableDamage += value;
        }

        if (totalApplicableDamage <= ent.Comp.ShedDamageThreshold)
            return;

        var triggerChance = (float) totalApplicableDamage * ent.Comp.ShedScalingChance;
        if (!_random.Prob(Math.Clamp(triggerChance, 0f, 1f)))
            return;

        var feather = SpawnFeather(ent, bloody: true);
        _physics.ApplyLinearImpulse(feather, _random.NextAngle().ToVec() * _random.NextFloat(10, 40));
        _physics.ApplyAngularImpulse(feather, _random.NextFloat(-30, 30));

        var meta = MetaData(feather);
        _metaData.SetEntityName(feather,
            Loc.GetString(ent.Comp.FeatherBloodiedNameString, ("item", Name(feather))), meta);
        _metaData.SetEntityDescription(feather, Loc.GetString(ent.Comp.FeatherBloodiedDescString), meta);
        Dirty(feather, meta);

        _popup.PopupEntity(Loc.GetString(ent.Comp.DroppedFeatherString), ent, ent, PopupType.MediumCaution);
        _chat.TryEmoteWithoutChat(ent, ent.Comp.ScreamEmote);
        _statusEffects.TryAddStatusEffect<IgnoreSlowOnDamageComponent>(ent, "Adrenaline",
            TimeSpan.FromSeconds(3), true);
    }

    /// <summary>Creates a feather carrying its owner's color and DNA.</summary>
    private EntityUid SpawnFeather(Entity<PreenableComponent> ent, bool bloody)
    {
        var feather = SpawnAtPosition(ent.Comp.FeatherPrototype.Id, Transform(ent).Coordinates);

        if (TryComp<HumanoidAppearanceComponent>(ent, out var appearance))
            _appearance.SetData(feather, FeatherVisuals.FeatherColor, appearance.SkinColor);

        _forensics.TransferDna(feather, ent, false);

        ent.Comp.CurrentFeathers--;
        ent.Comp.ReplenishTime = _timing.CurTime + ent.Comp.ReplenishDelay;
        Dirty(ent);

        if (!bloody || !TryComp<BloodstreamComponent>(ent, out var bloodstream) || bloodstream.BloodSolution == null)
            return feather;

        var solution = bloodstream.BloodSolution.Value.Comp.Solution;
        _appearance.SetData(feather, FeatherVisuals.BloodColor, solution.GetColor(_prototype));
        return feather;
    }

    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);

        var query = EntityQueryEnumerator<PreenableComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.ReplenishTime == null || _timing.CurTime < component.ReplenishTime ||
                component.CurrentFeathers >= component.MaximumFeathers)
                continue;

            component.CurrentFeathers++;
            if (component.CurrentFeathers >= component.MaximumFeathers)
                component.ReplenishTime = null;
            else
                component.ReplenishTime = _timing.CurTime + component.ReplenishDelay;

            Dirty(uid, component);
        }
    }
}
