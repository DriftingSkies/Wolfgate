#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Roles;
using Content.Server.Jobs;
using Content.Server.Radio;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Radio.Components;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Roles;

/// <summary>MMC jobs always use a spawner, so their station announcement is skipped; arrivals go on the MMC radio instead.</summary>
[TestFixture]
[TestOf(typeof(WGRadioOnJoinSystem))]
public sealed class WGRadioOnJoinTest
{
    [TestCase("MMCEmployee")]
    [TestCase("MMCSecurity")]
    [TestCase("MMCLiason")]
    public async Task ArrivalReachesFactionRadioTest(string job)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();
        var session = pair.Player!;

        await server.WaitAssertion(() =>
        {
            var probe = entMan.System<WGRadioOnJoinProbeSystem>();
            var station = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            var receiver = entMan.SpawnEntity(null, map.GridCoords);
            entMan.EnsureComponent<ActiveRadioComponent>(receiver).Channels.Add("Mieyo");
            entMan.EnsureComponent<WGRadioOnJoinProbeComponent>(receiver);

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<TelecomExemptComponent>(mob);
            foreach (var special in proto.Index<JobPrototype>(job).Special.OfType<AddComponentSpecial>())
            {
                special.AfterEquip(mob);
            }

            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            entMan.EventBus.RaiseLocalEvent(mob, new PlayerSpawnCompleteEvent(mob, session, job, false, false, 1, station, profile), true);
            var name = entMan.GetComponent<MetaDataComponent>(mob).EntityName;

            Assert.That(probe.Heard, Has.Count.EqualTo(1), $"{job} didn't report its arrival on the Mieyo channel");
            Assert.That(probe.Heard[0], Does.Contain(name));
            probe.Heard.Clear();

            entMan.EventBus.RaiseLocalEvent(mob, new PlayerSpawnCompleteEvent(mob, session, job, false, true, 2, station, profile), true);
            Assert.That(probe.Heard, Is.Empty, "a silent join was reported");

            entMan.DeleteEntity(mob);
            entMan.DeleteEntity(receiver);
            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }
}

/// <summary>Marks a test radio whose received messages are recorded.</summary>
[RegisterComponent]
public sealed partial class WGRadioOnJoinProbeComponent : Component;

/// <summary>Test-only listener for radio messages on probed receivers.</summary>
public sealed partial class WGRadioOnJoinProbeSystem : EntitySystem
{
    public readonly List<string> Heard = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGRadioOnJoinProbeComponent, RadioReceiveEvent>(OnReceive);
    }

    private void OnReceive(Entity<WGRadioOnJoinProbeComponent> ent, ref RadioReceiveEvent args)
    {
        Heard.Add(args.OriginalChatMsg.Message);
    }
}
