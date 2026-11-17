#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.ReaverCampaign.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.ReaverCampaign;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>The Contested Sector game mode: its preset, and the campaign following its rule.</summary>
[TestOf(typeof(WGReaverCampaignRuleSystem))]
public sealed class WGReaverGameModeTest : WGReaverTestBase
{
    private static ProtoId<GamePresetPrototype> WGContestedSectorProto => "WGContestedSector";
    private static ProtoId<GamePresetPrototype> MonoStandardProto => "MonoStandard";

    /// <summary>The preset is votable and is Standard's rules plus the Contested Sector rule and the ADS rule.</summary>
    [Test]
    public async Task ContestedSectorPresetIsStandardPlusTheRules()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var protos = Server.ResolveDependency<IPrototypeManager>();
            var ashfall = protos.Index<GamePresetPrototype>(WGContestedSectorProto);
            var standard = protos.Index<GamePresetPrototype>(MonoStandardProto);
            Assert.That(ashfall.ShowInVote, Is.True);
            Assert.That(ashfall.Rules, Is.SupersetOf(standard.Rules));
            Assert.That(ashfall.Rules, Does.Contain("WGReaverCampaignRule"));
            Assert.That(ashfall.Rules, Does.Contain("WGAdsCampaignRule"));
            Assert.That(ashfall.Rules, Does.Contain("WGRemadeDiscoveryRule"));
            Assert.That(ashfall.Rules.Count, Is.EqualTo(standard.Rules.Count + 3));
        });
    }

    /// <summary>With the cvar at its default the campaign idles; the rule running turns it on.</summary>
    [Test]
    public async Task CampaignFollowsTheRule()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var config = Server.ResolveDependency<IConfigurationManager>();
            var ticker = Server.System<GameTicker>();
            var campaign = Server.System<WGReaverCampaignSystem>();
            config.SetCVar(ReaverCampaignCVars.Enabled, false);
            config.SetCVar(EncountersCVars.Enabled, true);

            Assert.That(campaign.Idle(), Is.EqualTo(WGReaverIdle.CampaignOff));
            Assert.That(campaign.Running, Is.False);

            ticker.StartGameRule("WGReaverCampaignRule", out var rule);
            Assert.That(ticker.IsGameRuleActive<WGReaverCampaignRuleComponent>(), Is.True);
            Assert.That(campaign.Idle(), Is.Not.EqualTo(WGReaverIdle.CampaignOff));
            Assert.That(campaign.Running, Is.True);

            ticker.EndGameRule(rule);
            Assert.That(campaign.Idle(), Is.EqualTo(WGReaverIdle.CampaignOff));
        });
    }
}
