#nullable enable
using Content.Server._WF.Xenonids.Name;
using Content.Shared._WF.Xenonids.MassScanner;
using Content.Shared._WF.Xenonids.Name;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A xeno names itself once with a short tag, and every xeno carries a mass scanner action.</summary>
[TestFixture]
public sealed class XenoNicknameTest
{
    [Test]
    public async Task NameOnceWithFourLetters()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var nicknames = entities.System<XenoNicknameSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var drone = entities.SpawnEntity("CMXenoDrone", map.GridCoords);
            var nickname = entities.GetComponent<XenoNicknameComponent>(drone);
            Assert.That(nickname.ActionEntity, Is.Not.Null, "An unnamed xeno has the naming action.");

            Assert.That(nicknames.TrySetNickname(drone, "toolong", out _), Is.False, "Five or more characters are refused.");
            Assert.That(nicknames.TrySetNickname(drone, "a b", out _), Is.False, "Only letters and digits.");
            Assert.That(nicknames.TrySetNickname(drone, "wolf", out _), "Four letters are fine.");
            Assert.That(entities.GetComponent<MetaDataComponent>(drone).EntityName, Does.Contain("WOLF-"), "The tag replaces XX in the name.");
            Assert.That(nicknames.TrySetNickname(drone, "fang", out _), Is.False, "Only once.");
            Assert.That(entities.GetComponent<XenoNameComponent>(drone).Named);
            Assert.That(entities.GetComponent<XenoNicknameComponent>(drone).ActionEntity, Is.Null, "The action goes away once used.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryXenoHasAMassScanner()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var ui = entities.System<UserInterfaceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "CMXenoDrone", "CMXenoQueen", "CMXenoLarva", "CMXenoParasite" })
            {
                var xeno = entities.SpawnEntity(id, map.GridCoords);
                Assert.That(entities.HasComponent<RadarConsoleComponent>(xeno), $"{id} is a radar console.");
                Assert.That(ui.HasUi(xeno, RadarConsoleUiKey.Key), $"{id} has the radar window.");
                Assert.That(entities.GetComponent<XenoMassScannerComponent>(xeno).ActionEntity, Is.Not.Null, $"{id} has the scanner action.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
