#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Ward;
using Content.Shared._WF.RMCCore.Tether;
using Content.Shared._WF.Xenonids.Hook;
using Content.Shared.Body.Organ;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the 2026-11-07 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1107Test
{
    /// <summary>Report 571: a character name keeps its digits with restricted names on.</summary>
    [Test]
    public async Task NamesKeepDigits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var restricted = cfg.GetCVar(CCVars.RestrictedNames);

        await server.WaitAssertion(() =>
        {
            cfg.SetCVar(CCVars.RestrictedNames, true);
            var session = server.PlayerMan.Sessions.First();
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithName("Kel-1234");
            profile.EnsureValid(session, server.InstanceDependencyCollection);
            Assert.That(profile.Name, Is.EqualTo("Kel-1234"));
            cfg.SetCVar(CCVars.RestrictedNames, restricted);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 570: a shield pushing someone out of its oval moves the body, not the organs inside it.</summary>
    [Test]
    public async Task ShieldLeavesOrgansInTheBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var ward = server.System<WFWardSystem>();
        var containers = server.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(40f, 0f), map.MapId));
            var organs = new List<EntityUid>();
            var query = em.EntityQueryEnumerator<OrganComponent>();
            while (query.MoveNext(out var organ, out _))
            {
                if (containers.IsEntityInContainer(organ))
                    organs.Add(organ);
            }
            Assert.That(organs, Is.Not.Empty, "The test human has no organs.");

            var turned = ward.HoldOut(map.Grid, new Vector2(40f, 0f), new Vector2(20f, 20f), new HashSet<EntityUid>(), true);
            Assert.That(turned, Is.EqualTo(1), "Only the body should be pushed out.");
            Assert.Multiple(() =>
            {
                foreach (var organ in organs)
                    Assert.That(containers.IsEntityInContainer(organ), Is.True, $"{em.ToPrettyString(organ)} was pulled out.");
            });
            em.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 575: an abductor's hook lets go on its own when the pull never lands, as in space.</summary>
    [Test]
    public async Task HookLetsGoWithoutLanding()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var hooks = server.System<XenoHookSystem>();
        var map = await pair.CreateTestMap();
        EntityUid target = default;

        await server.WaitAssertion(() =>
        {
            var xeno = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(10f, 0f), map.MapId));
            target = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(14f, 0f), map.MapId));
            var hook = em.EnsureComponent<XenoHookComponent>(xeno);
            Assert.That(hooks.TryHookTarget((xeno, hook), target), Is.True);
            Assert.That(em.HasComponent<RMCTetherComponent>(target), Is.True);
        });

        await server.WaitRunTicks((int) (server.Timing.TickRate * (XenoHookSystem.MaxHold.TotalSeconds + 0.5)));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<XenoHookedComponent>(target), Is.False, "The hook never let go.");
            Assert.That(em.HasComponent<RMCTetherComponent>(target), Is.False, "The hook's line is still drawn.");
        });

        await pair.CleanReturnAsync();
    }
}
