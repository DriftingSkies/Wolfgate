#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.MappingMode;
using Content.Server.GameTicking;
using Content.Shared._WF.MappingMode;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// With the mode on the server never leaves the lobby, loads no station, and makes maps only for sessions.
/// </summary>
[TestFixture]
public sealed class WFMappingTickerTest
{
    [Test]
    public async Task StaysInTheLobby()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = entMan.System<GameTicker>();
        var mapping = entMan.System<WFMappingModeSystem>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await Enable(pair);

        int Maps() => entMan.EntityQuery<MapComponent>(true).Count();

        void AssertLobby(string when)
        {
            Assert.Multiple(() =>
            {
                Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby), when);
                Assert.That(ticker.Paused, Is.True, $"The lobby countdown is paused {when}.");
                Assert.That(entMan.EntityQuery<StationDataComponent>(true).Count(), Is.Zero, $"No station {when}.");
            });
        }

        await server.WaitAssertion(() =>
        {
            AssertLobby("once the mode is on");
            Assert.That(server.CfgMan.GetCVar(CCVars.DefaultGuide), Is.EqualTo(WFMappingGuide.Root.Id),
                "The guidebook opens on the mapping guide.");
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() => AssertLobby("after waiting"));

        await server.WaitPost(() => ticker.RestartRound());
        await pair.RunTicksSync(10);

        var maps = 0;
        var rules = 0;
        await server.WaitAssertion(() =>
        {
            AssertLobby("after a restart");
            Assert.That(mapping.Sessions, Is.Empty);
            maps = Maps();
        });

        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            AssertLobby("after waiting past the restart");
            Assert.That(Maps(), Is.EqualTo(maps), "Nothing loads a map until a session exists.");
        });

        var a = (await AddMappers(pair, 1))[0];
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Ticker Test", out _), Is.True);
            Assert.That(Maps(), Is.EqualTo(maps + 1), "A session makes exactly its own map.");

            // A forced round start would load no station.
            var list = prototypes.EnumeratePrototypes<GameMapPrototype>().Take(1).ToList();
            Assert.That(list, Is.Not.Empty);
            var ev = new LoadingMapsEvent(list);
            entMan.EventBus.RaiseEvent(EventSource.Local, ev);
            Assert.That(ev.Maps, Is.Empty);

            // A forced start, as startround or the server API would, starts nothing: no rules, no sector.
            rules = entMan.EntityQuery<GameRuleComponent>(true).Count();
            ticker.StartRound(force: true);
        });

        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            AssertLobby("after a forced round start");
            Assert.Multiple(() =>
            {
                Assert.That(Maps(), Is.EqualTo(maps + 1), "A forced start loads no map.");
                Assert.That(entMan.EntityQuery<GameRuleComponent>(true).Count(), Is.EqualTo(rules),
                    "A forced start adds no game rule.");
            });
        });

        await Disable(pair);
        await server.WaitAssertion(() =>
        {
            Assert.That(ticker.Paused, Is.False, "Turning the mode off lets the lobby run again.");
            Assert.That(mapping.Sessions, Is.Empty, "Turning the mode off closes every session.");
            Assert.That(server.CfgMan.GetCVar(CCVars.DefaultGuide), Is.EqualTo(CCVars.DefaultGuide.DefaultValue),
                "Turning the mode off gives the guidebook its usual first page back.");
        });

        await pair.CleanReturnAsync();
    }
}
