using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    /// <summary>Space a battery needs clear round its point.</summary>
    private const float BatteryClearance = 80f;

    /// <summary>How far round the ring, in radians, a blocked battery point is nudged each try.</summary>
    private const float BatteryNudge = 0.2f;

    /// <summary>The batteries standing on the sector map.</summary>
    public List<Entity<WFReaverBatteryComponent, WFEncounterComponent>> Batteries()
    {
        var map = _territory.SectorMap;
        var batteries = new List<Entity<WFReaverBatteryComponent, WFEncounterComponent>>();
        var query = EntityQueryEnumerator<WFReaverBatteryComponent, WFEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var battery, out var encounter, out var xform))
        {
            if (encounter.Resolution == null && xform.MapID == map && !TerminatingOrDeleted(uid))
                batteries.Add((uid, battery, encounter));
        }

        return batteries;
    }

    /// <summary>The batteries of one stronghold.</summary>
    public List<Entity<WFReaverBatteryComponent, WFEncounterComponent>> BatteriesOf(EntityUid stronghold)
    {
        return Batteries().Where(battery => battery.Comp1.Stronghold == stronghold).ToList();
    }

    /// <summary>Rings a hub with the bracket's batteries at once; the ring is kept up from then on.</summary>
    private void Fortify(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, EntityUid stronghold)
    {
        if (!TryComp<WFReaverStrongholdComponent>(stronghold, out var comp))
            return;

        comp.Fortified = true;
        comp.BatteryBearing = _random.NextFloat(MathF.Tau);
        comp.BatterySlots = Math.Max(comp.BatterySlots, Bracket(campaign, state).Batteries);
        for (var slot = 0; slot < comp.BatterySlots; slot++)
        {
            TrySpawnBattery(stronghold, slot);
        }

        comp.NextBattery = _timing.CurTime + ReinforcementInterval(campaign);
    }

    /// <summary>Grows each fortified hub's ring with the population and rebuilds one lost battery a hub at a time, never under fire.</summary>
    private void TickBatteries(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var bracket = Bracket(campaign, state);
        var batteries = Batteries();
        foreach (var (uid, comp, _) in Strongholds())
        {
            if (!comp.Fortified)
                continue;

            comp.BatterySlots = Math.Max(comp.BatterySlots, bracket.Batteries);
            if (now < comp.NextBattery || UnderFire(comp))
                continue;

            var taken = batteries.Where(battery => battery.Comp1.Stronghold == uid).Select(battery => battery.Comp1.Slot).ToHashSet();
            var free = Enumerable.Range(0, comp.BatterySlots).Where(slot => !taken.Contains(slot)).ToList();
            if (free.Count == 0)
                continue;

            comp.NextBattery = now + (TrySpawnBattery(uid, free[0]) ? ReinforcementInterval(campaign) : ReinforceRetry);
        }
    }

    /// <summary>Places a battery in a hub's ring at its slot's bearing, nudged round until the point is clear.</summary>
    public bool TrySpawnBattery(EntityUid stronghold, int slot)
    {
        if (Campaign is not { } campaign
            || !TryComp<WFReaverStrongholdComponent>(stronghold, out var comp)
            || !TryComp<WFEncounterComponent>(stronghold, out var hub) || hub.Resolution != null
            || !_prototypes.TryIndex(campaign.Battery, out var prototype) || prototype.Ships.Count == 0)
            return false;

        var map = Transform(stronghold).MapID;
        var centre = Position(hub, comp.Lead);
        var bearing = comp.BatteryBearing + MathF.Tau * slot / Math.Max(1, comp.BatterySlots);
        for (var i = 0; i < 8; i++)
        {
            var angle = bearing + BatteryNudge * ((i + 1) / 2) * (i % 2 == 0 ? 1 : -1);
            var point = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * campaign.BatteryRange;
            if (!_scheduler.IsClearSpace(map, point, BatteryClearance, 0f)
                || !SpawnEncounter(prototype, new MapCoordinates(point, map), out var uid, offBudget: true))
                continue;

            var battery = AddComp<WFReaverBatteryComponent>(uid);
            battery.Stronghold = stronghold;
            battery.Slot = slot;
            battery.Lead = prototype.Ships[0].Key;
            Log.Info($"Reaver battery {ToPrettyString(uid)} placed in slot {slot} round hub {ToPrettyString(stronghold)}.");
            return true;
        }

        return false;
    }

    /// <summary>Breaks batteries whose lead is out of the fight or whose hub is gone, and strengthens each hub's shield while a battery of its stands.</summary>
    private void TendBatteries(WFReaverCampaignPrototype? campaign)
    {
        var standing = new HashSet<EntityUid>();
        foreach (var (uid, comp, encounter) in Batteries())
        {
            if (!TryComp<WFEncounterComponent>(comp.Stronghold, out var hub) || hub.Resolution != null)
            {
                _encounters.End(uid);
                continue;
            }

            if (!encounter.Ships.TryGetValue(comp.Lead, out var lead) || !_encounters.InFight(lead))
            {
                _encounters.Resolve(uid, WFEncounterResolution.Destroyed);
                continue;
            }

            standing.Add(comp.Stronghold);
        }

        if (campaign == null)
            return;

        foreach (var (uid, comp, encounter) in Strongholds())
        {
            if (comp.Fortified && encounter.Ships.TryGetValue(comp.Lead, out var lead) && !lead.Virtual
                && ShieldOf(lead.Grid) is { } emitter && TryComp<ShipShieldEmitterComponent>(emitter, out var shield))
            {
                comp.ShieldHeal ??= shield.HealPerSecond;
                shield.HealPerSecond = comp.ShieldHeal.Value * (standing.Contains(uid) ? campaign.BatteryShieldHeal : 1f);
            }
        }
    }

    /// <summary>Destroyed, a battery pays its bounty, marks its attackers and holds back the next rebuild a full interval.</summary>
    private void BatteryResolved(Entity<WFReaverBatteryComponent> battery, WFEncounterResolution resolution)
    {
        var (uid, comp) = battery;
        var campaign = CampaignFor(State());
        if (resolution != WFEncounterResolution.Destroyed || campaign == null
            || !TryComp<WFEncounterComponent>(uid, out var encounter)
            || !TryComp<WFReaverStrongholdComponent>(comp.Stronghold, out var home))
            return;

        home.NextBattery = MaxTime(home.NextBattery, _timing.CurTime + ReinforcementInterval(campaign));
        Mark(encounter.ShipHits.Keys.Select(key => key.Attacker));
        var (bounty, paid) = PayBatteryBounty(campaign, uid);
        AnnounceRadio(Loc.GetString("wf-reaver-battery-destroyed",
            ("callsign", WFSectorHex.Callsign(home.Cell)),
            ("bounty", bounty),
            ("paid", paid)));
    }

    /// <summary>Takes a hub's batteries down with it: beaten, their hits count for the hub's bounty and their hulls go through the usual cleanup.</summary>
    private void SweepBatteries(EntityUid stronghold, bool fold)
    {
        foreach (var battery in BatteriesOf(stronghold))
        {
            if (fold && TryComp<WFEncounterComponent>(stronghold, out var hub))
                FoldHits(hub, battery.Comp2);

            if (fold)
                _encounters.Resolve(battery.Owner, WFEncounterResolution.Ended);
            else
                _encounters.End(battery.Owner);
        }
    }

    private static TimeSpan MaxTime(TimeSpan a, TimeSpan b)
    {
        return a > b ? a : b;
    }
}
