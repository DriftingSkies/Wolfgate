#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server.Silicons.Borgs;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the 2026-11-04 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1104Test
{
    /// <summary>
    /// Report 450: a TSFMC assault borg takes the advanced weapon module. Its two stock weapon modules shared the
    /// Security module id, so any third weapon module counted as a duplicate; no borg type ships with two of one id.
    /// </summary>
    [Test]
    public async Task FactionBorgTakesItsAdvancedWeaponModule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var type in protoMan.EnumeratePrototypes<BorgTypePrototype>())
            {
                var ids = new List<string>();
                foreach (var module in type.DefaultModules)
                {
                    if (protoMan.Index(module).TryGetComponent<ItemBorgModuleComponent>(out var item, entMan.ComponentFactory))
                        ids.Add(item.ModuleId);
                }

                Assert.That(ids, Is.Unique, $"Borg type {type.ID} ships with two modules of one id.");
            }

            var borgs = server.System<BorgSystem>();
            var borg = entMan.SpawnEntity("TSFBorgChassisAssault", map.GridCoords);
            var advanced = entMan.SpawnEntity("BorgModuleTSFAdvancedWeapon", map.GridCoords);
            var chassis = entMan.GetComponent<BorgChassisComponent>(borg);
            Assert.That(chassis.ModuleContainer.ContainedEntities, Has.Count.GreaterThanOrEqualTo(2), "The assault borg didn't get its stock modules.");
            Assert.That(borgs.CanInsertModule(borg, advanced, chassis), Is.True, "The advanced weapon module was refused.");

            var again = entMan.SpawnEntity("BorgModuleTSFStandardWeapon", map.GridCoords);
            Assert.That(borgs.CanInsertModule(borg, again, chassis), Is.False, "A second copy of a stock weapon module got in.");
        });

        await pair.CleanReturnAsync();
    }
}
