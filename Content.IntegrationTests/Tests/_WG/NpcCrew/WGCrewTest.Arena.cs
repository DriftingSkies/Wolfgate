#nullable enable
using System.Linq;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.Console;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>wg_crew arena spawns three crewed ships and queues the escort and the docking run.</summary>
    [Test]
    public async Task ArenaCommandSpawnsCrewedShips()
    {
        await Server.WaitPost(() =>
            Server.ResolveDependency<IConsoleHost>().ExecuteCommand(ServerSession, "wg_crew arena"));
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var crews = Server.System<WGCrewObjectiveSystem>().Snapshot()
                .Where(crew => crew.Group.StartsWith("arena-"))
                .ToDictionary(crew => crew.Group);
            Assert.That(crews.Keys, Is.EquivalentTo(new[] { "arena-lead", "arena-escort", "arena-dock" }));
            Assert.That(crews.Values.All(crew => crew.Alive >= 2), Is.True,
                string.Join(", ", crews.Values.Select(crew => $"{crew.Group}={crew.Alive}")));
            var lead = crews["arena-lead"].Grid;
            Assert.That(crews["arena-escort"].Objectives.Single().Kind, Is.EqualTo(WGCrewObjectiveKind.Escort));
            Assert.That(crews["arena-escort"].Objectives.Single().Target, Is.EqualTo(lead));
            Assert.That(crews["arena-dock"].Objectives.Single().Kind, Is.EqualTo(WGCrewObjectiveKind.Dock));
            Assert.That(crews["arena-dock"].Objectives.Single().Target, Is.EqualTo(lead));
            Assert.That(crews.Values.All(crew => crew.Settings.Battlegroup == "arena" && crew.Activity.Length > 0), Is.True);
            // The docking ship is no escort; only the battlegroup makes it an ally of the lead.
            Assert.That(Server.System<WGCrewEscortSystem>().AreInFormation(
                SEntMan.GetEntity(lead), SEntMan.GetEntity(crews["arena-dock"].Grid)), Is.True);
        });
    }
}
