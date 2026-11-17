using Content.Server.Station.Systems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared._WG.RoundSetup;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

/// <summary>Every civilian crew job receives its normal mandatory outfit and saved loadout selections.</summary>
[TestFixture]
public sealed class CrewLoadoutTest
{
    [Test]
    public async Task CrewJobsReceiveDefaultAndSavedLoadouts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var spawning = pair.Server.System<StationSpawningSystem>();
            var inventory = pair.Server.System<InventorySystem>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var job in WGShipCrewJobs.Jobs)
            {
                // A cyborg spawns as its own entity and wears nothing.
                if (prototypes.Index<JobPrototype>(job).JobEntity != null)
                    continue;

                var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithoutAllTraitPreferences().WithBankBalance(1000000);
                var mob = spawning.SpawnPlayerMob(map.GridCoords, new ProtoId<JobPrototype>(job), profile, null, session: pair.Player);
                foreach (var slot in new[] { "jumpsuit", "back", "shoes", "id" })
                    Assert.That(inventory.TryGetSlotEntity(mob, slot, out _), Is.True, $"{job} must spawn with {slot}.");
                pair.Server.EntMan.DeleteEntity(mob);
            }
            var saved = new RoleLoadout("JobStationEngineer");
            var custom = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            saved.SetDefault(custom, pair.Player, prototypes);
            saved.RemoveLoadout("StationEngineerJumpsuit", "StationEngineerJumpsuit", prototypes);
            saved.AddLoadout("StationEngineerJumpsuit", "StationEngineerJumpskirt", prototypes);
            custom = custom.WithLoadout(saved);
            var engineer = spawning.SpawnPlayerMob(map.GridCoords, new ProtoId<JobPrototype>("StationEngineer"), custom, null, session: pair.Player);
            Assert.That(inventory.TryGetSlotEntity(engineer, "jumpsuit", out var uniform), Is.True);
            Assert.That(pair.Server.EntMan.GetComponent<MetaDataComponent>(uniform!.Value).EntityPrototype!.ID, Is.EqualTo("ClothingUniformJumpskirtEngineering"));
            pair.Server.EntMan.DeleteEntity(engineer);
        });
        await pair.CleanReturnAsync();
    }
}
