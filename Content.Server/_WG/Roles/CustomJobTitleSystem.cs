using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Server.StationRecords.Systems;
using Content.Shared._WG.Roles;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Roles;

/// <summary>Puts a player's custom job title on their ID card, mind and station records when they spawn.</summary>
public sealed partial class CustomJobTitleSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IdCardSystem _idCard = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private StationRecordsSystem _records = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
        SubscribeLocalEvent<AfterGeneralRecordCreatedEvent>(OnGeneralRecordCreated);
    }

    /// <summary>Whether this body's mind carries a chosen title, such as a company rank, that a ship purchase must not replace.</summary>
    public bool HasCustomTitle(EntityUid body) =>
        _mind.TryGetMind(body, out var mindId, out _) && HasComp<CustomJobTitleComponent>(mindId);

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (CustomJobTitleRules.GetTitle(ev.Profile, ev.JobId, _prototype) is not { } title)
            return;

        // The mind carries it for everything that names the job: cryo, character menu, ghost warps.
        if (_mind.TryGetMind(ev.Mob, out var mindId, out _))
        {
            var custom = EnsureComp<CustomJobTitleComponent>(mindId);
            custom.Job = ev.JobId!;
            custom.Title = title;
        }

        if (_idCard.TryFindIdCard(ev.Mob, out var card))
            _idCard.TryChangeJobTitle(card, title, card.Comp);

        _adminLogger.Add(LogType.Identity, LogImpact.Low,
            $"{ToPrettyString(ev.Mob):player} spawned as {ev.JobId} with custom job title {title}");
    }

    /// <summary>Covers both the station record and the sector-wide one.</summary>
    private void OnGeneralRecordCreated(AfterGeneralRecordCreatedEvent ev)
    {
        if (CustomJobTitleRules.GetTitle(ev.Profile, ev.Record.JobPrototype, _prototype) is not { } title)
            return;

        ev.Record.JobTitle = title;
        _records.Synchronize(ev.Key);
    }
}
