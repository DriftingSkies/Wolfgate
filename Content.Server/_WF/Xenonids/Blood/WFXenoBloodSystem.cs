using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._WF.Xenonids.Blood;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenonids.Blood;

/// <summary>
/// RMC xenos have no bloodstream, so nothing marks a fight. This drops acid-blood decals when a xeno is hurt, when it
/// dies and when it is gibbed, and gibs a corpse that keeps taking damage so explosions leave gibs instead of a body.
/// </summary>
public sealed class WFXenoBloodSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly ProtoId<DamageGroupPrototype> Brute = "Brute";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFXenoBloodComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WFXenoBloodComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WFXenoBloodComponent, BeingGibbedEvent>(OnGibbed);
    }

    private void OnDamageChanged(Entity<WFXenoBloodComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        if (_mobState.IsDead(ent) && TryGibCorpse(ent, args.Damageable))
            return;

        var brute = _proto.Index(Brute);
        if (!args.DamageDelta.TryGetDamageInGroup(brute, out var bruteDelta) || bruteDelta < ent.Comp.DamagePerSplatter)
            return;

        if (_timing.CurTime < ent.Comp.NextSplatter)
            return;

        ent.Comp.NextSplatter = _timing.CurTime + ent.Comp.SplatterCooldown;
        Splatter(ent, 1);
    }

    private void OnMobStateChanged(Entity<WFXenoBloodComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            Splatter(ent, ent.Comp.DeathSplatters);
    }

    private void OnGibbed(Entity<WFXenoBloodComponent> ent, ref BeingGibbedEvent args)
    {
        Splatter(ent, ent.Comp.GibSplatters);
    }

    /// <summary>Gibs a dead xeno whose damage has climbed far past its death threshold.</summary>
    private bool TryGibCorpse(Entity<WFXenoBloodComponent> ent, DamageableComponent damageable)
    {
        if (!_thresholds.TryGetDeadThreshold(ent, out var dead) || dead <= 0)
            return false;

        if (damageable.TotalDamage < dead.Value * ent.Comp.GibDamageMultiplier)
            return false;

        _body.GibBody(ent);
        return true;
    }

    private void Splatter(EntityUid xeno, int count)
    {
        if (TerminatingOrDeleted(xeno))
            return;

        var comp = Comp<WFXenoBloodComponent>(xeno);
        var coordinates = _transform.GetMoverCoordinates(xeno);
        for (var i = 0; i < count; i++)
        {
            var spawner = Spawn(comp.Splatter, coordinates);
            _transform.AttachToGridOrMap(spawner);
        }
    }
}
