using Content.Server._WF.WolfChem.Narcotics;
using Content.Server._WF.Voice;
using Content.Shared._WF.Voice;
using Content.Shared._WF.WolfChem.Narcotics;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.WolfChem.Narcotics;

/// <summary>
/// Heavy highs change what is said (rushed, trailed, hazy, gushing or muddled); the voice speeds up or slows down with
/// the phase, and a sober speaker's voice is left alone.
/// </summary>
[TestFixture]
[TestOf(typeof(WFDrugSpeechSystem))]
public sealed class DrugSpeechTest
{
    [Test]
    public async Task SpeechTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var speech = server.System<WFDrugSpeechSystem>();
            var intoxication = server.System<WFIntoxicationSystem>();

            for (var i = 0; i < 20; i++)
            {
                var rushed = speech.Rush("We need to go now. Right now.", false);
                Assert.That(rushed, Does.EndWith("now!"), rushed);
                Assert.That(rushed, Does.Not.Contain("."), rushed);

                var trailed = speech.Trail("I feel fine, really fine.", false);
                Assert.That(trailed, Does.EndWith("fine..."), trailed);
                Assert.That(trailed, Does.StartWith("I"), trailed);

                var runOn = speech.Rush("Wait, we need to go now, right now.", true);
                Assert.That(runOn, Does.Not.Contain(",").And.Not.Contain("."), runOn);

                var dropped = speech.Trail("I feel really fine, really fine.", true);
                Assert.That(dropped, Does.StartWith("I").And.EndWith("fine..."), dropped);

                var hazed = speech.Haze("So the thing is we should go.", false);
                Assert.That(hazed, Does.StartWith("So "), hazed);

                var gushed = speech.Gush("That was so good. Thank you.", false);
                Assert.That(gushed, Does.EndWith("~").And.Not.Contain("."), gushed);

                const string line = "the stars are singing to me now";
                var muddled = speech.Muddle(line);
                Assert.That(muddled.Split(' '), Is.EquivalentTo(line.Split(' ')), "Muddling only moves words.");
            }

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entMan.EnsureComponent<WFIntoxicationComponent>(mob);
            comp.Levels["WFCocaine"] = 2f;
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant), Is.EqualTo(WFDrugPhase.Light));
            comp.Levels["WFCocaine"] = 10f;
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant), Is.EqualTo(WFDrugPhase.Heavy));
            comp.Levels["WFCrack"] = 13f;
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant), Is.EqualTo(WFDrugPhase.Overdose),
                "The furthest profile of the family sets the phase.");
            Assert.That(intoxication.FamilyPhase(comp, WFDrugFamily.Opioid), Is.EqualTo(WFDrugPhase.None));

            SpeechVoiceModifyEvent Speak()
            {
                var ev = new SpeechVoiceModifyEvent(new SpeechVoice());
                entMan.EventBus.RaiseLocalEvent(mob, ref ev);
                return ev;
            }

            var normal = new SpeechVoice().Speed;
            var fast = Speak();
            Assert.That(fast.Changed, Is.True);
            Assert.That(fast.Voice.Speed, Is.LessThan(normal), "An overdosed stimulant voice talks faster.");

            comp.Levels.Clear();
            comp.Levels["WFHeroin"] = 10f;
            Assert.That(Speak().Voice.Speed, Is.GreaterThan(normal), "A heavy opioid voice drags.");

            comp.Levels.Clear();
            Assert.That(Speak().Changed, Is.False, "Sober, the voice is untouched.");
        });

        await pair.CleanReturnAsync();
    }
}
