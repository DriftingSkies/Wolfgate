#nullable enable
using System.Linq;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.SafetyDepositBox;

/// <summary>
/// The Damocles, the Godfather's exclusive sidearm, is on every safety deposit box's tag blacklist and is still a
/// sidearm.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositBoxComponent))]
public sealed class SafetyDepositBoxDamoclesTest
{
    private const string DamoclesProto = "WeaponPistolDamocles";
    private const string SidearmTag = "Sidearm";

    [Test]
    public async Task BoxesRefuseTheDamocles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        var entMan = server.EntMan;
        var factory = entMan.ComponentFactory;
        var tagSys = entMan.System<TagSystem>();

        await server.WaitAssertion(() =>
        {
            var boxes = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(proto => !proto.Abstract && proto.TryGetComponent<SafetyDepositBoxComponent>(out _, factory))
                .ToList();
            Assert.That(boxes, Is.Not.Empty, "No safety deposit box prototypes were found.");

            var gun = server.ProtoMan.Index<EntityPrototype>(DamoclesProto);
            Assert.That(gun.TryGetComponent<TagComponent>(out var gunTags, factory), Is.True, "The Damocles has no tags.");

            Assert.Multiple(() =>
            {
                // A child's tag list replaces its parent's, so the blacklist tag must not cost it the inherited one.
                Assert.That(tagSys.HasTag(gunTags!, SidearmTag), Is.True, "The Damocles is no longer a sidearm.");

                foreach (var proto in boxes)
                {
                    Assert.That(proto.TryGetComponent<StorageComponent>(out var storage, factory), Is.True,
                        $"{proto.ID} has no storage.");

                    var refused = storage!.Blacklist?.Tags;
                    Assert.That(refused != null && tagSys.HasAnyTag(gunTags!, refused), Is.True,
                        $"{proto.ID} takes the Damocles.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
