#nullable enable
using System.Linq;
using Content.Client._WF.MarkingToggles;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.MarkingToggles;

/// <summary>A player hides and shows their own species and custom markings mid-round.</summary>
[TestFixture]
[TestOf(typeof(Server._WF.MarkingToggles.MarkingToggleSystem))]
public sealed class MarkingToggleTest
{
    private const string MobProto = "MobHuman";
    private const string Hair = "HumanHairAfro";
    private const int MaxWaitTicks = 60;

    private static readonly CustomMarking Custom = new(new string('7', CustomMarkingRules.HashLength), CustomMarkingPlacement.Skin);

    [Test]
    public async Task TogglesOwnMarkings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var humanoidSys = entMan.System<SharedHumanoidAppearanceSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);
        var body = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = entMan.SpawnEntity(MobProto, map.GridCoords);
            humanoidSys.AddMarking(body, Hair, Color.Red, forced: true);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(body);
            humanoid.CustomMarkings.Add(Custom);
            entMan.Dirty(body, humanoid);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, body);
        });
        await pair.RunTicksSync(5);

        var clientBody = client.EntMan.GetEntity(server.EntMan.GetNetEntity(body));
        var toggles = client.System<MarkingToggleSystem>();

        // The verb is on the player's own body.
        var verbText = client.ResolveDependency<ILocalizationManager>().GetString("wf-marking-toggle-verb");
        await client.WaitAssertion(() =>
        {
            var verbs = client.System<SharedVerbSystem>().GetLocalVerbs(clientBody, clientBody, typeof(Verb));
            Assert.That(verbs.Any(v => v.Text == verbText), Is.True);
        });

        await client.WaitPost(() =>
        {
            toggles.SetVisible(MarkingCategories.Hair, Hair, false);
            toggles.SetVisible(Custom, false);
        });
        await WaitFor(pair, () => !HairVisible(client.EntMan, clientBody) && Hidden(client.EntMan, clientBody),
            "the client to see both markings hidden");

        await server.WaitAssertion(() =>
        {
            Assert.That(HairVisible(entMan, body), Is.False);
            Assert.That(Hidden(entMan, body), Is.True);
            Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(body).CustomMarkings, Does.Contain(Custom),
                "a hidden custom marking is still worn");
        });

        await client.WaitPost(() =>
        {
            toggles.SetVisible(MarkingCategories.Hair, Hair, true);
            toggles.SetVisible(Custom, true);
        });
        await WaitFor(pair, () => HairVisible(client.EntMan, clientBody) && !Hidden(client.EntMan, clientBody),
            "the client to see both markings shown again");

        await pair.CleanReturnAsync();
    }

    private static bool HairVisible(IEntityManager entMan, EntityUid body)
    {
        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(body);
        return humanoid.MarkingSet.Markings.TryGetValue(MarkingCategories.Hair, out var hair)
               && hair.Single(m => m.MarkingId == Hair).Visible;
    }

    private static bool Hidden(IEntityManager entMan, EntityUid body)
    {
        return entMan.GetComponent<HumanoidAppearanceComponent>(body).HiddenCustomMarkings.Contains(Custom);
    }

    private static async Task WaitFor(Pair.TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks && !condition(); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), Is.True, $"Timed out waiting for {what}.");
    }
}
