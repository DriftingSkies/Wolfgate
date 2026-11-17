using Content.Server._WF.ShipTiers;
using Content.Shared._Mono.Company;
using Content.Shared._WF.SpecialistKits;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WF.SpecialistKits;

/// <summary>
/// Holds job-locked specialist kits to their jobs and caps how many of a faction hold each limited kit, by members
/// online. A holder who leaves the round frees the slot.
/// </summary>
public sealed class WFSpecialistKitSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private WFShipTierSystem _shipTiers = default!;

    /// <summary>Players who drew each kit this round, by faction and kit.</summary>
    private readonly Dictionary<(string Company, string Kit), HashSet<NetUserId>> _holders = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSpecialistKitVendAttemptEvent>(OnVendAttempt);
        SubscribeLocalEvent<WFSpecialistKitVendedEvent>(OnVended);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _holders.Clear());
    }

    /// <summary>How many of a limited kit the faction may take with this many members online.</summary>
    public static int Limit(WFSpecialistKitComponent kit, int members)
    {
        return Math.Min(kit.MaxSlots, 1 + members / Math.Max(kit.MembersPerSlot, 1));
    }

    private void OnVendAttempt(ref WFSpecialistKitVendAttemptEvent args)
    {
        if (!_protos.TryIndex(args.Entry, out var proto)
            || !proto.TryGetComponent(out WFSpecialistKitComponent? kit, _factory))
            return;

        if (kit.Jobs.Count > 0
            && (!_mind.TryGetMind(args.User, out var mind, out _)
                || !_jobs.MindTryGetJobId(mind, out var job)
                || job is not { } jobId
                || !kit.Jobs.Contains(jobId)))
        {
            args.Cancelled = true;
            args.Reason = "wf-specialist-kit-job";
            return;
        }

        if (!TryComp<CompanyComponent>(args.User, out var company)
            || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        var holders = _holders.GetOrNew((company.CompanyName.Id, args.Entry.Id));
        if (kit.Limited
            && !holders.Contains(actor.PlayerSession.UserId)
            && Active(holders, company.CompanyName) >= Limit(kit, _shipTiers.MembersOnline(company.CompanyName)))
            args.Cancelled = true;
    }

    /// <summary>Records the holder once the vendor has handed the kit out, so a vend its own checks refuse costs no slot.</summary>
    private void OnVended(ref WFSpecialistKitVendedEvent args)
    {
        if (!_protos.TryIndex(args.Entry, out var proto)
            || !proto.TryGetComponent(out WFSpecialistKitComponent? _, _factory)
            || !TryComp<CompanyComponent>(args.User, out var company)
            || !TryComp<ActorComponent>(args.User, out var actor))
            return;

        _holders.GetOrNew((company.CompanyName.Id, args.Entry.Id)).Add(actor.PlayerSession.UserId);
    }

    /// <summary>Whether this player drew the kit this round, for buying a replacement.</summary>
    public bool Holds(EntityUid user, EntProtoId kit)
    {
        return TryComp<CompanyComponent>(user, out var company)
               && TryComp<ActorComponent>(user, out var actor)
               && _holders.TryGetValue((company.CompanyName.Id, kit.Id), out var holders)
               && holders.Contains(actor.PlayerSession.UserId);
    }

    /// <summary>
    /// Holders still in the round: their mind owns a body of the faction, connected or not, so a disconnect never
    /// frees a slot a reconnect would take back.
    /// </summary>
    private int Active(HashSet<NetUserId> holders, string company)
    {
        var count = 0;
        foreach (var id in holders)
        {
            if (_mind.TryGetMind(id, out var mind)
                && mind.Value.Comp.OwnedEntity is { } body
                && TryComp<CompanyComponent>(body, out var member)
                && member.CompanyName == company)
                count++;
        }
        return count;
    }
}
