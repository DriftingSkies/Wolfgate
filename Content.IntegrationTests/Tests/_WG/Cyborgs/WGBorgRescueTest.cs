using System.Linq;
using System.Numerics;
using Content.Client._WG.Cyborgs;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.Components;
using Content.Shared._WG.Cyborgs;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

[TestFixture]
[TestOf(typeof(WGBorgRescueSystem))]
public sealed class WGBorgRescueTest
{
    [Test]
    public async Task NearbyPatientGetsBuckleVerbWithoutHandsAndCanBeUnbuckled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        EntityUid borg = default;
        EntityUid patient = default;
        BuckleComponent buckle = null!;
        StrapComponent strap = null!;

        await server.WaitAssertion(() =>
        {
            borg = entMan.SpawnEntity("BorgChassisSelectable", map.GridCoords);
            patient = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            buckle = entMan.GetComponent<BuckleComponent>(patient);
            strap = entMan.GetComponent<StrapComponent>(borg);

            Assert.That(entMan.GetComponent<HandsComponent>(borg).Count, Is.Zero);
            Assert.That(strap.Position, Is.EqualTo(StrapPosition.Stand));

            var verbs = server.System<SharedVerbSystem>().GetLocalVerbs(patient, borg, typeof(InteractionVerb));
            var buckleVerb = verbs.OfType<InteractionVerb>().SingleOrDefault(verb => verb.Category == VerbCategory.Buckle);
            Assert.That(buckleVerb, Is.Not.Null);

            buckleVerb!.Act!.Invoke();
            Assert.That(buckle.BuckledTo, Is.Null, "Buckling starts a delay.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            Assert.That(buckle.BuckledTo, Is.Null, "The patient is not buckled before the delay completes.");
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1.5f));

        await server.WaitAssertion(() =>
        {
            Assert.That(buckle.BuckledTo, Is.EqualTo(borg));
            Assert.That(strap.BuckledEntities, Does.Contain(patient));

            var verbs = server.System<SharedVerbSystem>().GetLocalVerbs(patient, borg, typeof(InteractionVerb));
            var unbuckleVerb = verbs.OfType<InteractionVerb>().SingleOrDefault(verb => verb.Category == null);
            Assert.That(unbuckleVerb, Is.Not.Null);

            unbuckleVerb!.Act!.Invoke();
            Assert.That(buckle.BuckledTo, Is.Null);
            Assert.That(strap.BuckledEntities, Does.Not.Contain(patient));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MovingPatientCancelsBorgBuckleDoAfter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid borg = default;
        EntityUid patient = default;
        BuckleComponent buckle = null!;
        await server.WaitAssertion(() =>
        {
            borg = entMan.SpawnEntity("BorgChassisSelectable", map.GridCoords);
            patient = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            buckle = entMan.GetComponent<BuckleComponent>(patient);

            var verbs = server.System<SharedVerbSystem>().GetLocalVerbs(patient, borg, typeof(InteractionVerb));
            var buckleVerb = verbs.OfType<InteractionVerb>().SingleOrDefault(verb => verb.Category == VerbCategory.Buckle);
            Assert.That(buckleVerb, Is.Not.Null);
            buckleVerb!.Act!.Invoke();
            Assert.That(buckle.BuckledTo, Is.Null);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(0.25f));
        await server.WaitAssertion(() =>
        {
            server.System<SharedTransformSystem>().SetCoordinates(patient, map.GridCoords.Offset(new Vector2(2f, 0f)));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2.5f));

        await server.WaitAssertion(() => Assert.That(buckle.BuckledTo, Is.Null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PatientDrawDepthFollowsBorgFacingAndRestoresOnUnbuckle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid borg = default;
        EntityUid patient = default;
        NetEntity borgNet = default;
        NetEntity patientNet = default;
        await server.WaitAssertion(() =>
        {
            borg = entMan.SpawnEntity("BorgChassisSelectable", map.GridCoords);
            patient = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1f, 0f)));
            server.System<SharedTransformSystem>().SetLocalRotation(borg, Direction.North.ToAngle());
            borgNet = entMan.GetNetEntity(borg);
            patientNet = entMan.GetNetEntity(patient);
        });

        await pair.RunTicksSync(10);

        var originalDepth = 0;
        var originalOffset = Vector2.Zero;
        var frontViewOffset = 0f;
        await client.WaitAssertion(() =>
        {
            var clientPatient = client.EntMan.GetEntity(patientNet);
            var patientSprite = client.EntMan.GetComponent<SpriteComponent>(clientPatient);
            originalDepth = patientSprite.DrawDepth;
            originalOffset = patientSprite.Offset;
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(patient, null, borg, popup: false), Is.True);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientBorg = client.EntMan.GetEntity(borgNet);
            var clientPatient = client.EntMan.GetEntity(patientNet);
            var borgDepth = client.EntMan.GetComponent<SpriteComponent>(clientBorg).DrawDepth;
            var patientSprite = client.EntMan.GetComponent<SpriteComponent>(clientPatient);
            Assert.Multiple(() =>
            {
                Assert.That(patientSprite.DrawDepth, Is.EqualTo(borgDepth + 2), "The back view draws the patient in front of the borg.");
                Assert.That(patientSprite.Offset.Y - originalOffset.Y, Is.EqualTo(0.4f).Within(0.1f), "Only the back view lifts the patient sprite.");
            });
        });

        await server.WaitPost(() => server.System<SharedTransformSystem>().SetLocalRotation(borg, Direction.South.ToAngle()));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientBorg = client.EntMan.GetEntity(borgNet);
            var clientPatient = client.EntMan.GetEntity(patientNet);
            var borgDepth = client.EntMan.GetComponent<SpriteComponent>(clientBorg).DrawDepth;
            var patientSprite = client.EntMan.GetComponent<SpriteComponent>(clientPatient);
            frontViewOffset = patientSprite.Offset.Y;
            Assert.Multiple(() =>
            {
                Assert.That(patientSprite.DrawDepth, Is.EqualTo(borgDepth - 1), "The front view draws the patient behind the borg.");
                Assert.That(frontViewOffset, Is.EqualTo(originalOffset.Y).Within(0.1f), "The front view removes the back-view lift.");
            });
        });

        await server.WaitPost(() => server.System<SharedTransformSystem>().SetLocalRotation(borg, Direction.East.ToAngle()));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientBorg = client.EntMan.GetEntity(borgNet);
            var clientPatient = client.EntMan.GetEntity(patientNet);
            var borgDepth = client.EntMan.GetComponent<SpriteComponent>(clientBorg).DrawDepth;
            var patientSprite = client.EntMan.GetComponent<SpriteComponent>(clientPatient);
            Assert.Multiple(() =>
            {
                Assert.That(patientSprite.DrawDepth, Is.EqualTo(borgDepth + 1), "Side views draw the patient in front of the borg.");
                Assert.That(patientSprite.Offset.Y, Is.EqualTo(frontViewOffset + 0.2f).Within(0.1f), "Side views lift the patient sprite.");
            });
        });

        await server.WaitPost(() => server.System<SharedBuckleSystem>().TryUnbuckle(patient, null, popup: false));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var clientPatient = client.EntMan.GetEntity(patientNet);
            var patientSprite = client.EntMan.GetComponent<SpriteComponent>(clientPatient);
            Assert.Multiple(() =>
            {
                Assert.That(patientSprite.DrawDepth, Is.EqualTo(originalDepth));
                Assert.That(patientSprite.Offset.Y, Is.EqualTo(frontViewOffset).Within(0.1f));
            });
        });

        await pair.CleanReturnAsync();
    }
}
