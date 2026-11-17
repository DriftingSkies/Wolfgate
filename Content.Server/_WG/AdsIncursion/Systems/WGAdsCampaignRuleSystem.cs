using Content.Server._WG.AdsIncursion.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._WG.AdsIncursion.Systems;

/// <summary>Logs the ADS rule starting and ending; <see cref="WGAdsCampaignSystem.Idle"/> reads whether it is active.</summary>
public sealed class WGAdsCampaignRuleSystem : GameRuleSystem<WGAdsCampaignRuleComponent>
{
    protected override void Started(EntityUid uid, WGAdsCampaignRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        Log.Info("ADS incursion rule started: the campaign is on.");
    }

    protected override void Ended(EntityUid uid, WGAdsCampaignRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        Log.Info("ADS incursion rule ended: the campaign is off.");
    }
}
