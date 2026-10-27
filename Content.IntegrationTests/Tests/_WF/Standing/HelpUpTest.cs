#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Shared._WF.Standing;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Standing;

/// <summary>
/// Hugging a stunned, knocked-down body cuts both effects, once per cooldown, with or without a hug popup.
/// </summary>
[TestFixture]
[TestOf(typeof(WFHelpUpSystem))]
public sealed class HelpUpTest : GameTest
{
    private static readonly TimeSpan Paralyze = TimeSpan.FromSeconds(20);

    private TimeSpan End(EntityUid body, string key)
    {
        Assert.That(SEntMan.System<StatusEffectsSystem>().TryGetTime(body, key, out var time), Is.True, $"{key} is gone.");
        return time!.Value.Item2;
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task HelpCutsStunTest(bool withHugPopup)
    {
        var map = await WolfmedGameTest.CreateTestMap(Pair);
        EntityUid helper = default, body = default;
        TimeSpan stun = default, knock = default;

        await Server.WaitAssertion(() =>
        {
            helper = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            if (!withHugPopup)
                SEntMan.RemoveComponent<InteractionPopupComponent>(body);

            Assert.That(SEntMan.System<SharedStunSystem>().TryParalyze(body, Paralyze, true), Is.True);
            stun = End(body, "Stun");
            knock = End(body, "KnockedDown");

            var interaction = SEntMan.System<SharedInteractionSystem>();
            interaction.InteractHand(helper, body);
            // Inside the cooldown: no further cut.
            interaction.InteractHand(helper, body);

            Assert.That(End(body, "Stun"), Is.EqualTo(stun - WFHelpUpSystem.HelpAmount));
            Assert.That(End(body, "KnockedDown"), Is.EqualTo(knock - WFHelpUpSystem.HelpAmount));
        });

        await RunSeconds((float) WFHelpUpSystem.HelpCooldown.TotalSeconds + 0.5f);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedInteractionSystem>().InteractHand(helper, body);
            Assert.That(End(body, "Stun"), Is.EqualTo(stun - 2 * WFHelpUpSystem.HelpAmount));
            Assert.That(End(body, "KnockedDown"), Is.EqualTo(knock - 2 * WFHelpUpSystem.HelpAmount));
        });
    }
}
