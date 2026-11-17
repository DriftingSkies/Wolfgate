using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server._WG.AdsIncursion.Systems;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Shared._WG.Encounters;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Defcon;

/// <summary>The built-in DEFCON threat sources: the ADS and Reaver campaigns, the war level, open encounters and player deaths.</summary>
public sealed partial class WGDefconSourcesSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private WGAdsCampaignSystem _ads = default!;
    [Dependency] private WGReaverCampaignSystem _reaver = default!;

    private readonly List<TimeSpan> _deaths = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGDefconGatherEvent>(OnGather);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobState);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _deaths.Clear());
    }

    private void OnMobState(MobStateChangedEvent ev)
    {
        if (ev.NewMobState == MobState.Dead && HasComp<ActorComponent>(ev.Target))
            _deaths.Add(_timing.CurTime);
    }

    private void OnGather(WGDefconGatherEvent ev)
    {
        var profile = ev.Profile;

        if (_ads.State() is { Finished: false } ads)
            ev.Add(ByTier(profile.AdsTier, ads.Tier), Loc.GetString("wg-defcon-reason-ads", ("tier", _ads.TierName(ads.Tier))));

        if (_reaver.State() is { Finished: false } reaver)
            ev.Add(ByTier(profile.ReaverTier, reaver.Tier), Loc.GetString("wg-defcon-reason-reaver", ("tier", _reaver.TierName(reaver.Tier))));

        if (TryComp<WarLevelComponent>(_sectorService.GetServiceEntity(), out var war) && war.PostWar)
        {
            ev.Add(profile.WarHot, Loc.GetString("wg-defcon-reason-war"));
            ev.Floor = Math.Min(ev.Floor, profile.WarFloor);
        }

        var threats = 0;
        var distress = 0;
        var query = EntityQueryEnumerator<WGEncounterComponent>();
        while (query.MoveNext(out var encounter))
        {
            // Campaign ships start by hand and are scored through their campaign.
            if (encounter.Resolution != null
                || !_prototypes.TryIndex(encounter.Prototype, out var proto)
                || proto.Start != WGEncounterStart.Scheduled)
                continue;

            if (proto.Category == WGEncounterCategory.Threat)
                threats++;
            else if (proto.Category == WGEncounterCategory.Distress)
                distress++;
        }

        ev.Add(MathF.Min(threats * profile.ThreatEncounter, profile.ThreatEncounterCap),
            Loc.GetString("wg-defcon-reason-threats", ("count", threats)));
        ev.Add(MathF.Min(distress * profile.Distress, profile.DistressCap),
            Loc.GetString("wg-defcon-reason-distress", ("count", distress)));

        _deaths.RemoveAll(time => ev.Now - time > profile.DeathWindow);
        ev.Add(MathF.Min(_deaths.Count * profile.Death, profile.DeathCap),
            Loc.GetString("wg-defcon-reason-deaths", ("count", _deaths.Count)));
    }

    private static float ByTier(List<float> scores, int tier)
    {
        return scores.Count == 0 ? 0f : scores[Math.Clamp(tier, 0, scores.Count - 1)];
    }
}
