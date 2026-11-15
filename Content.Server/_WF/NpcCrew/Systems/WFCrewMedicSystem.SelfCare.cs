using Content.Server._WF.NpcCrew.Components;
using Content.Server.NPC.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Server.Medical.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// NPC crew look after their own minor wounds out of a fight: every <see cref="SelfCareGap"/> a hurt crewman patches
/// himself up with one item, bleeding first. Anyone hurt past <see cref="SelfCareCallDamage"/> also counts as calling
/// for a medic, so the ship's medics come to him (<see cref="Priority"/>).
/// </summary>
public sealed partial class WFCrewMedicSystem
{
    /// <summary>How often a hurt crewman treats himself.</summary>
    private static readonly TimeSpan SelfCareGap = TimeSpan.FromSeconds(8);

    /// <summary>Damage past which a crewman counts as calling for a medic.</summary>
    private const float SelfCareCallDamage = 40f;

    /// <summary>What a crewman treats himself with, in order: bleeding, bruises, cuts, burns.</summary>
    private static readonly EntProtoId[] SelfCareItems = { "Gauze", "Brutepack", "MedicatedSuture", "RegenerativeMesh" };

    /// <summary>One of each self-care item, kept off the map.</summary>
    private readonly Dictionary<EntProtoId, EntityUid> _selfSupplies = new();

    private readonly Dictionary<EntityUid, TimeSpan> _nextSelfCare = new();
    private TimeSpan _nextSelfCareSweep;

    private void UpdateSelfCare()
    {
        var now = _timing.CurTime;
        if (now < _nextSelfCareSweep)
            return;

        _nextSelfCareSweep = now + TimeSpan.FromSeconds(2);
        var crew = EntityQueryEnumerator<WFCrewComponent, DamageableComponent>();
        while (crew.MoveNext(out var uid, out _, out var damageable))
        {
            if (damageable.TotalDamage <= FixedPoint2.Zero || !_mobState.IsAlive(uid) || Down(uid)
                || HasComp<ActorComponent>(uid) || HasComp<NPCRangedCombatComponent>(uid)
                || HasComp<NPCMeleeCombatComponent>(uid)
                || _nextSelfCare.TryGetValue(uid, out var next) && now < next)
                continue;

            _nextSelfCare[uid] = now + SelfCareGap;
            foreach (var item in SelfCareItems)
            {
                if (SelfSupply(item) is not { } supply || !TryComp<HealingComponent>(supply, out var healing)
                    || !_healing.IsWoundDamaged((uid, damageable), healing, null))
                    continue;

                _woundHealing.TryApplyHealing(uid, null, (supply, healing), uid, out _, out _);
                break;
            }
        }

        foreach (var uid in new List<EntityUid>(_nextSelfCare.Keys))
        {
            if (TerminatingOrDeleted(uid))
                _nextSelfCare.Remove(uid);
        }
    }

    /// <summary>A crewman hurt badly enough to want a medic, though still on his feet.</summary>
    private bool WantsMedic(EntityUid patient)
    {
        return HasComp<WFCrewComponent>(patient) && !HasComp<ActorComponent>(patient)
               && CompOrNull<DamageableComponent>(patient)?.TotalDamage > FixedPoint2.New(SelfCareCallDamage);
    }

    private EntityUid? SelfSupply(EntProtoId item)
    {
        if (_selfSupplies.TryGetValue(item, out var held) && !TerminatingOrDeleted(held))
            return held;

        var spawned = Spawn(item, MapCoordinates.Nullspace);
        _selfSupplies[item] = spawned;
        return spawned;
    }
}
