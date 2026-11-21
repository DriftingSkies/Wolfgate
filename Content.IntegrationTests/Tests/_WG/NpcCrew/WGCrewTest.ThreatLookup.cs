#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>A crewman's look round serves his checks only briefly: a hostile that steps up is picked within the cache time.</summary>
    [Test]
    public async Task NearbyLookupIsReusedOnlyBriefly()
    {
        var deck = await CreateDeck(new Vector2(6, 40), 5, gravity: true);
        var crew = EntityUid.Invalid;
        var hostile = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var weapons = Server.System<WGCrewWeaponSystem>();
            crew = ConvoyCrew(deck, "WGCrewDeckhand", "lookup", new Vector2(1.5f));
            Assert.That(weapons.PickTarget(crew), Is.Null, "Precondition: nobody about.");

            hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(3.5f, 1.5f)));
            Assert.That(weapons.CanSee(crew, hostile), Is.True, "Precondition: the hostile is in sight.");
            Assert.That(weapons.PickTarget(crew), Is.Null, "The look round just taken is reused.");
        });

        await RunTicks(Ticks(WGCrewWeaponSystem.NearbyCacheTime.TotalSeconds) + 1);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewWeaponSystem>().PickTarget(crew), Is.EqualTo(hostile),
                "Once it lapses the next look finds him.");
        });
    }
}
