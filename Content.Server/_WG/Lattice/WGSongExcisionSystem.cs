using Content.Shared._Onyx.Body.Systems;
using Content.Server.Administration.Logs;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WG.Lattice;
using Content.Shared._WG.Wolfmed.Surgery;
using Content.Shared.Database;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Content.Shared.Stacks;
using Robust.Shared.Random;

namespace Content.Server._WG.Lattice;

/// <summary>
/// Cutting the song out of a Choir member's head. It uses up a shard of exotic matter and hurts the brain; most of
/// the time the patient wakes free of the Choir, and sometimes the song takes them on the table and they rise Remade.
/// </summary>
public sealed class WGSongExcisionSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private OrganHealthSystem _organHealth = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private WGDeepRisingSystem _rising = default!;
    [Dependency] private WolfmedSurgeryConditionSystem _conditions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSongExcisionEffectComponent, SurgeryStepEvent>(OnStep);
    }

    private void OnStep(Entity<WGSongExcisionEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (!HasComp<WGChoirMemberComponent>(args.Body))
            return;

        foreach (var tool in args.Tools)
        {
            if (HasComp<WGSongExcisionToolComponent>(tool))
            {
                _stacks.Use(tool, 1);
                break;
            }
        }

        if (_conditions.TryFindOrgan(args.Part, "brain", out var brain))
            _organHealth.ChangeHealth(brain, -ent.Comp.BrainDamage);

        if (_random.Prob(ent.Comp.FailChance))
        {
            Fail(args.Body, args.User);
            return;
        }

        Free(args.Body, args.User);
    }

    /// <summary>The song is out: the patient leaves the Choir and can be revived again.</summary>
    public void Free(EntityUid body, EntityUid surgeon)
    {
        RemComp<WGChoirMemberComponent>(body);
        if (_mind.TryGetMind(body, out var mindId, out _))
            _roles.MindTryRemoveRole<WGChoirRoleComponent>(mindId);

        if (_inventory.TryGetSlotEntity(body, "id", out var pda))
            RemComp<WGChoirMarkComponent>(pda.Value);

        _popup.PopupEntity(Loc.GetString("wg-song-excision-freed-surgeon"), body, surgeon, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("wg-song-excision-freed"), body, body, PopupType.LargeCaution);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(surgeon):player} cut the song out of {ToPrettyString(body):player}; they left the Choir");
    }

    /// <summary>The song keeps them: they die on the table and rise Remade.</summary>
    private void Fail(EntityUid body, EntityUid surgeon)
    {
        _popup.PopupEntity(Loc.GetString("wg-song-excision-failed"), body, PopupType.LargeCaution);
        _adminLog.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(surgeon):player} failed to cut the song out of {ToPrettyString(body):player}; they are turning Remade");
        _mobState.ChangeMobState(body, MobState.Dead);
        _rising.Raise(body, false);
    }
}
