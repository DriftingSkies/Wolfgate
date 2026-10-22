using System.Collections.Generic;
using Content.Server._WF.CorticalBorer;
using Content.Shared._Common.Consent;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.CorticalBorer;

/// <summary>
/// The borer entry toggle starts on, an explicit "off" refuses borers, and only the non-default state is stored.
/// </summary>
[TestFixture]
[TestOf(typeof(BorerEntryConsentSystem))]
public sealed class BorerEntryConsentTest
{
    private const string HostProto = "MobHuman";
    private const string BorerProto = "MobCorticalBorer";

    [Test]
    public async Task ToggleGatesEntry()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var entry = entMan.System<BorerEntryConsentSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var borer = entMan.SpawnEntity(BorerProto, map.GridCoords);
            var host = entMan.SpawnEntity(HostProto, map.GridCoords);
            Assert.That(entry.Allows(borer, host), Is.True, "A body with no consent settings refused a borer.");

            var consent = entMan.EnsureComponent<ConsentComponent>(host);
            Assert.That(entry.Allows(borer, host), Is.True, "The toggle didn't default to on.");

            consent.ConsentSettings = new PlayerConsentSettings(string.Empty,
                new Dictionary<ProtoId<ConsentTogglePrototype>, string> { [BorerEntryConsentSystem.Toggle] = "off" });
            Assert.That(entry.Allows(borer, host), Is.False, "Turning the toggle off didn't refuse the borer.");

            var off = new PlayerConsentSettings(string.Empty,
                new Dictionary<ProtoId<ConsentTogglePrototype>, string> { [BorerEntryConsentSystem.Toggle] = "off" });
            off.EnsureValid(cfg, protoMan);
            Assert.That(off.Toggles.ContainsKey(BorerEntryConsentSystem.Toggle), Is.True, "An off state wasn't stored.");

            var on = new PlayerConsentSettings(string.Empty,
                new Dictionary<ProtoId<ConsentTogglePrototype>, string> { [BorerEntryConsentSystem.Toggle] = "on" });
            on.EnsureValid(cfg, protoMan);
            Assert.That(on.Toggles.ContainsKey(BorerEntryConsentSystem.Toggle), Is.False, "The default state was stored.");

            entMan.DeleteEntity(borer);
            entMan.DeleteEntity(host);
        });

        await pair.CleanReturnAsync();
    }
}
