using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ShipCommand;
using Content.Server.Shuttles.Events;
using Content.Shared._Onyx.Wounds;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;

namespace Content.Server._WG.CrewBarks;

public sealed partial class WGCrewBarkSystem
{
    private readonly List<Entity<WGCrewBarkerComponent>> _speakers = new();

    private void InitializeReactions()
    {
        // After friendly fire's protection, which may cancel the hit, and before the wounds lose its origin.
        SubscribeLocalEvent<WGCrewBarkerComponent, BeforeDamageChangedEvent>(OnBeforeDamage,
            after: [typeof(WGCrewFriendlyFireSystem)], before: [typeof(WoundDamageRoutingSystem)]);
        SubscribeLocalEvent<WGCrewBarkerComponent, DisarmedEvent>(OnDisarmed);
        SubscribeLocalEvent<WGCrewBarkerComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<WGCrewBarkerComponent, WGShipPostRelievedEvent>(OnRelieved);
        SubscribeLocalEvent<WGCrewTreatedEvent>(OnTreated);
        SubscribeLocalEvent<WGCrewAlertEvent>(OnAlert);
        SubscribeLocalEvent<WGCrewBarksComponent, FTLStartedEvent>(OnJump);
        SubscribeLocalEvent<WGCrewBarksComponent, FTLCompletedEvent>(OnArrived);
    }

    private void OnBeforeDamage(Entity<WGCrewBarkerComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || args.Origin is not { } origin)
            return;

        Struck(ent, _crew.Wielder(origin));
    }

    private void OnDisarmed(Entity<WGCrewBarkerComponent> ent, ref DisarmedEvent args)
    {
        Struck(ent, args.Source);
    }

    /// <summary>The Boss's blow is taken with thanks (and forgiven by the crew's own rules); anyone else's is not.</summary>
    private void Struck(Entity<WGCrewBarkerComponent> ent, EntityUid attacker)
    {
        if (attacker == ent.Owner || TerminatingOrDeleted(attacker) || !HasComp<MobStateComponent>(attacker)
            || _crew.SameCrew(ent, attacker))
            return;

        Standing(ent.Comp.Grid, attacker, out var boss);
        Bark(ent.AsNullable(), boss ? WGCrewBark.Struck : WGCrewBark.Attacked);
    }

    private void OnDamageChanged(Entity<WGCrewBarkerComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased || args.DamageDelta is not { } delta
            || !TryComp<WGCrewBarksComponent>(ent.Comp.Grid, out var barks)
            || -delta.GetTotal().Float() < barks.HealedAt)
            return;

        Bark(ent.AsNullable(), WGCrewBark.Healed);
    }

    private void OnTreated(ref WGCrewTreatedEvent args)
    {
        if (TryComp<WGCrewBarkerComponent>(args.Patient, out var barker))
            Bark((args.Patient, barker), WGCrewBark.Healed);
    }

    /// <summary>A crewman relieved of his post says his own word in place of the roster's.</summary>
    private void OnRelieved(Entity<WGCrewBarkerComponent> ent, ref WGShipPostRelievedEvent args)
    {
        ent.Comp.Arriving = false;
        var said = args.By is { } by
            ? Bark(ent.AsNullable(), WGCrewBark.Relieved, ("name", Identity.Name(by, EntityManager, ent.Owner)))
            : Bark(ent.AsNullable(), WGCrewBark.StoodDown);
        if (said)
            args.Spoke = true;
    }

    /// <summary>When the crew find a hostile, one of them who sees it calls it out.</summary>
    private void OnAlert(ref WGCrewAlertEvent args)
    {
        if (!HasComp<WGCrewBarksComponent>(args.Grid))
            return;

        var query = EntityQueryEnumerator<WGCrewBarkerComponent, WGCrewComponent>();
        while (query.MoveNext(out var uid, out var barker, out var crew))
        {
            if (barker.Grid != args.Grid || crew.Group != args.Group)
                continue;

            foreach (var hostile in args.Hostiles)
            {
                if (_weapons.CanSee(uid, hostile) && Bark((uid, barker), WGCrewBark.Fight))
                    return;
            }
        }
    }

    private void OnJump(Entity<WGCrewBarksComponent> grid, ref FTLStartedEvent args)
    {
        Announce(grid, WGCrewBark.Jump);
    }

    private void OnArrived(Entity<WGCrewBarksComponent> grid, ref FTLCompletedEvent args)
    {
        Announce(grid, WGCrewBark.Arrived);
    }

    /// <summary>Someone near each player aboard says it; the area gap keeps it to one voice per spot.</summary>
    private void Announce(Entity<WGCrewBarksComponent> grid, WGCrewBark kind)
    {
        GatherAboard();
        if (!_aboard.TryGetValue(grid, out var aboard))
            return;

        _speakers.Clear();
        var query = EntityQueryEnumerator<WGCrewBarkerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var barker, out var xform))
        {
            if (barker.Grid == grid.Owner && xform.GridUid == grid.Owner && Near(xform, aboard, grid.Comp.HearRange))
                _speakers.Add((uid, barker));
        }

        _random.Shuffle(_speakers);
        foreach (var speaker in _speakers)
            Bark(speaker.AsNullable(), kind);
    }
}
