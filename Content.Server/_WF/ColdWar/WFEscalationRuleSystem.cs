using Content.Server._Mono.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Audio;
using Robust.Shared.Timing;

namespace Content.Server._WF.ColdWar;

/// <summary>
/// Counts down to the end of TSF/PDV talks, warns the sector on the way and then sets the war level HOT for good.
/// </summary>
public sealed class WFEscalationRuleSystem : GameRuleSystem<WFEscalationRuleComponent>
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WarLevelSystem _warLevel = default!;

    private static readonly SoundSpecifier BreakSound = new SoundPathSpecifier("/Audio/Misc/gamma.ogg");

    protected override void Started(EntityUid uid, WFEscalationRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        component.BreakAt = _timing.CurTime + component.BreakDelay;
        component.WarningsSent = 0;
        component.Broken = false;
    }

    protected override void ActiveTick(EntityUid uid, WFEscalationRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.BreakAt is not { } at)
            return;

        var now = _timing.CurTime;
        // Only the latest due warning is worth announcing; the rest are stale.
        TimeSpan? due = null;
        while (component.WarningsSent < component.Warnings.Count && now >= at - component.Warnings[component.WarningsSent])
        {
            due = component.Warnings[component.WarningsSent++];
        }

        if (now >= at)
        {
            Break(uid, component);
            return;
        }

        if (due is not { } lead)
            return;

        var minutes = Math.Max(1, (int) Math.Round(lead.TotalMinutes));
        var key = _warLevel.GetWarLevel(uid) ? "wf-escalation-warning-hot" : "wf-escalation-warning";
        _chat.DispatchGlobalAnnouncement(Loc.GetString(key, ("minutes", minutes)),
            sender: Loc.GetString("war-level-announcement-sender"),
            colorOverride: Color.Orange);
    }

    /// <summary>Whether talks have broken down under any running Escalation rule.</summary>
    public bool TalksBroken()
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out _, out var comp, out _))
        {
            if (comp.Broken)
                return true;
        }

        return false;
    }

    private void Break(EntityUid uid, WFEscalationRuleComponent component)
    {
        component.BreakAt = null;
        component.Broken = true;

        if (!_warLevel.GetWarLevel(uid))
        {
            _warLevel.SetLevel(true);
            return;
        }

        // Already at war: the level stays, but the sector should know it can no longer be called off.
        _chat.DispatchGlobalAnnouncement(Loc.GetString("wf-escalation-broken"),
            sender: Loc.GetString("war-level-announcement-sender"),
            announcementSound: BreakSound,
            colorOverride: Color.Crimson);
    }
}
