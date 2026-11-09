using System.Linq;
using Content.Server._WF.OutpostIndustry;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.PlanetMap;

namespace Content.Server._WF.Colonies;

/// <summary>
/// Heritage: a round counts for an outpost when someone is home long enough in it. Five rounds lower its reload markup,
/// fifteen add two cryopods and a quarter to its producers' cap, thirty lower the markup again and write its name on the
/// planet map. A sale resets it.
/// </summary>
public sealed partial class WFColonySystem
{
    public const int HeritageMarkup = 5;
    public const int HeritageSettled = 15;
    public const int HeritageLandmark = 30;

    private void InitializeHeritage()
    {
        SubscribeLocalEvent<WFColonyMemberComponent, WFOutpostMarkupEvent>(OnMarkup);
        SubscribeLocalEvent<WFColonyMemberComponent, WFOutpostPodLimitEvent>(OnPodLimit);
        SubscribeLocalEvent<WFColonyMemberComponent, WFOutpostProducerCapEvent>(OnProducerCap);
    }

    /// <summary>Counts this round toward an outpost's heritage once someone has been home long enough.</summary>
    private void UpdateHeritage(Entity<WFColonyMemberComponent> member)
    {
        if (member.Comp.HeritageRound == _ticker.RoundId || member.Comp.HomeMinutes < _cfg.GetCVar(ColonyCVars.HeritageMinutes))
            return;

        member.Comp.HeritageRound = _ticker.RoundId;
        member.Comp.Heritage++;
    }

    /// <summary>An outpost's heritage: the rounds that counted.</summary>
    public int Heritage(EntityUid outpost) => CompOrNull<WFColonyMemberComponent>(outpost)?.Heritage ?? 0;

    private void OnMarkup(Entity<WFColonyMemberComponent> member, ref WFOutpostMarkupEvent args)
    {
        if (member.Comp.Heritage >= HeritageLandmark)
            args.Markup = MathF.Min(args.Markup, 1.3f);
        else if (member.Comp.Heritage >= HeritageMarkup)
            args.Markup = MathF.Min(args.Markup, 1.4f);
    }

    private void OnPodLimit(Entity<WFColonyMemberComponent> member, ref WFOutpostPodLimitEvent args)
    {
        if (member.Comp.Heritage >= HeritageSettled)
            args.Limit += 2;
    }

    private void OnProducerCap(Entity<WFColonyMemberComponent> member, ref WFOutpostProducerCapEvent args)
    {
        if (member.Comp.Heritage >= HeritageSettled)
            args.Cap *= 1.25;
    }

    /// <summary>Writes the names of a world's landmark outposts on its planet map.</summary>
    private void UpdateLandmarks(EntityUid network, List<OutpostEntry> entries)
    {
        var orbit = Comp<WFPlanetNetworkComponent>(network).OrbitMap;
        if (!TryComp<WFPlanetMapComponent>(orbit, out var map))
            return;

        var labels = entries
            .Where(entry => Heritage(entry.Outpost) >= HeritageLandmark)
            .Select(entry => new WFPlanetMapLabel(_transform.GetWorldPosition(entry.Outpost.Comp1.Console), Name(entry.Outpost)))
            .ToList();
        if (labels.SequenceEqual(map.Labels))
            return;

        map.Labels = labels;
        DirtyField(orbit, map, nameof(WFPlanetMapComponent.Labels));
    }
}
