using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server._WG.ColdWar;
using Content.Shared._Mono.Company;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Mono.WarDeclarator;

public sealed partial class ManualPortstrikeRuleSystem
{
    [Dependency] private IGameTiming _wgTiming = default!;
    [Dependency] private SectorServiceSystem _wgSectorService = default!;
    [Dependency] private WGEscalationRuleSystem _wgEscalation = default!;

    protected override void Started(EntityUid uid, ManualPortstrikeRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var state = EnsureComp<WGColdWarComponent>(uid);
        state.AnnounceAt = _wgTiming.CurTime + state.AnnounceDelay;
    }

    protected override void ActiveTick(EntityUid uid, ManualPortstrikeRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (!TryComp<WGColdWarComponent>(uid, out var state) || state.AnnounceAt is not { } at || _wgTiming.CurTime < at)
            return;

        // Announcing the level it already has tells the round where it stands without changing anything.
        state.AnnounceAt = null;
        if (TryComp<WarLevelComponent>(_wgSectorService.GetServiceEntity(), out var level))
            _warLevelSystem.SetLevel(level.PostWar);
    }

    /// <summary>
    /// Flips the stance of the declarator's faction: a declaration of war or its withdrawal while the ceasefire
    /// holds, a ceasefire offer or its withdrawal during a war. The level moves once every faction asks for the
    /// same change. Always handles the use.
    /// </summary>
    private bool WgUseDeclarator(Entity<FactionWarDeclaratorComponent> ent, EntityUid user)
    {
        var faction = ent.Comp.Faction;
        var now = _wgTiming.CurTime;
        // Read once: with two rules for the same factions, the second must not see the level the first has just set.
        var level = _warLevelSystem.GetWarLevel(ent);
        // Escalation: once talks break down, a war cannot be called off.
        if (level && _wgEscalation.TalksBroken())
        {
            _popup.PopupEntity(Loc.GetString("wg-escalation-declarator-locked"), ent, user);
            return true;
        }

        var linked = false;
        var announced = false;
        var moved = false;
        var query = EntityQueryEnumerator<ManualPortstrikeRuleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.SectorStatus.ContainsKey(faction))
                continue;

            linked = true;
            var state = EnsureComp<WGColdWarComponent>(uid);
            if (state.NextUse.TryGetValue(faction, out var next) && now < next)
            {
                var seconds = (int) Math.Ceiling((next - now).TotalSeconds);
                _popup.PopupEntity(Loc.GetString("wg-cold-war-declarator-cooldown", ("seconds", seconds)), ent, user);
                continue;
            }

            var atWar = level == comp.WarLevel;
            // A level set from outside (an admin, a timed rule) leaves every stance behind it: bring them along.
            if (WgAllWant(comp, !atWar))
                WgSetAll(comp, atWar);

            var wantsWar = !comp.SectorStatus[faction];
            comp.SectorStatus[faction] = wantsWar;
            state.NextUse[faction] = now + state.Cooldown;

            if (!announced)
            {
                announced = true;
                var name = _prototypeManager.Index(faction).Name;
                var message = (atWar, wantsWar) switch
                {
                    (false, true) => Loc.GetString(ent.Comp.WarDeclarationMessage),
                    (false, false) => Loc.GetString("wg-cold-war-declaration-withdrawn", ("faction", name)),
                    (true, false) => Loc.GetString("wg-cold-war-ceasefire-offered", ("faction", name)),
                    (true, true) => Loc.GetString("wg-cold-war-ceasefire-withdrawn", ("faction", name)),
                };
                _radio.SendRadioMessage(ent, message, _prototypeManager.Index(ent.Comp.Channel), ent);
            }

            if (!moved && WgAllWant(comp, !atWar))
            {
                moved = true;
                _warLevelSystem.SetLevel(atWar ? !comp.WarLevel : comp.WarLevel);
            }
        }

        if (!linked)
            _popup.PopupEntity(Loc.GetString("wg-cold-war-declarator-unlinked"), ent, user);

        return true;
    }

    /// <summary>Whether every faction's stance is the given one.</summary>
    private static bool WgAllWant(ManualPortstrikeRuleComponent comp, bool war)
    {
        foreach (var stance in comp.SectorStatus.Values)
        {
            if (stance != war)
                return false;
        }

        return true;
    }

    private static void WgSetAll(ManualPortstrikeRuleComponent comp, bool war)
    {
        foreach (var faction in new List<ProtoId<CompanyPrototype>>(comp.SectorStatus.Keys))
        {
            comp.SectorStatus[faction] = war;
        }
    }
}
