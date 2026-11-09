#nullable enable
using System.Threading.Tasks;
using Content.Client._WF.SectorControl;
using Content.IntegrationTests.Tests._WF.SectorControl;
using Content.Server._WF.Colonies;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Colonies;

/// <summary>Colony cells on the sector map: a company's runtime faction and the neutral colonies faction claim cells and draw.</summary>
[TestOf(typeof(WFColonySystem))]
public sealed class ColonyCellsTest : WFSectorTestBase
{
    private static readonly WFSectorCell CompanyCell = new(3, -1);
    private static readonly WFSectorCell NeutralCell = new(-3, 1);
    private const string CompanyFaction = "WFColonyCompany900001";
    private const string CompanyName = "Test Colonial's worlds";

    /// <summary>
    /// A faction registered at runtime claims a cell and reaches the client with its name and colour; the neutral colonies
    /// faction draws from its prototype.
    /// </summary>
    [Test]
    public async Task CompanyAndNeutralCellsDraw()
    {
        await StartRound();
        var colour = Color.FromHex("#C08040");
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WFSectorTerritorySystem>();
            var map = MapData.MapId;
            territory.RegisterFaction(CompanyFaction, CompanyName, colour);
            Assert.That(territory.TryClaim(map, CompanyCell, CompanyFaction, null), Is.True, "A company's faction should claim a cell.");
            Assert.That(territory.TryClaim(map, NeutralCell, WFColonySystem.ColonyFaction, null), Is.True);

            var ev = Server.System<WFSectorSyncSystem>().Build(map);
            Assert.That(ev.Styles[CompanyFaction], Is.EqualTo(new WFSectorFactionStyle(CompanyName, colour)));
            Assert.That(ev.Styles.ContainsKey(WFColonySystem.ColonyFaction.Id), Is.False, "A prototype faction is drawn from its prototype.");
        });

        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.System<WFSectorClientSystem>().TryGetMap(MapData.MapId, out var view), Is.True);
            var company = view.Owners[CompanyCell];
            Assert.That(view.Colors[company], Is.EqualTo(colour));
            Assert.That(view.Names[company], Is.EqualTo(CompanyName));
            Assert.That(view.Fills[company], Has.Length.EqualTo(3 * 6), "The company's cell should be filled.");

            var neutral = view.Owners[NeutralCell];
            Assert.That(view.Colors[neutral], Is.EqualTo(Color.FromHex("#8FBF7F")));
            Assert.That(view.Names[neutral], Is.EqualTo(Client.ResolveDependency<ILocalizationManager>().GetString("wf-sector-faction-colonies")));
            Assert.That(view.Fills[neutral], Has.Length.EqualTo(3 * 6), "The neutral cell should be filled.");
        });

        await Server.WaitPost(() => Server.System<WFSectorTerritorySystem>().Clear(MapData.MapId));
    }
}
