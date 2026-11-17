using Content.Server._WG.ReaverCampaign.Components;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._WG.ReaverCampaign.Systems;

/// <summary>Logs the Contested Sector rule starting and ending; <see cref="WGReaverCampaignSystem.Idle"/> reads whether it is active.</summary>
public sealed class WGReaverCampaignRuleSystem : GameRuleSystem<WGReaverCampaignRuleComponent>
{
    protected override void Started(EntityUid uid, WGReaverCampaignRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);
        Log.Info("Contested Sector game mode started: the Reaver campaign is on.");
    }

    protected override void Ended(EntityUid uid, WGReaverCampaignRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);
        Log.Info("Contested Sector game mode ended: the Reaver campaign is off.");
    }
}
