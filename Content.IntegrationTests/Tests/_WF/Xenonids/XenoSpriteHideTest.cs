#nullable enable
using System.Collections.Generic;
using Content.Shared._WF.Xenonids.Burrow;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A xeno sprite another system hides (the external shuttle view hides every mob) stays hidden; no xeno system re-shows it each tick.</summary>
[TestFixture]
public sealed class XenoSpriteHideTest
{
    [Test]
    public async Task HiddenXenoSpritesStayHidden()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        var protos = new[] { "CMXenoDrone", "CMXenoQueen", "CMXenoBurrower", "CMXenoLurker", "CMXenoParasite", "CMXenoLarva", "RMCXenoCrusherCharger" };
        var uids = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            foreach (var proto in protos)
                uids.Add(server.EntMan.SpawnEntity(proto, map.GridCoords));
        });
        await pair.RunTicksSync(10);

        await client.WaitPost(() =>
        {
            var sprites = client.EntMan.System<SpriteSystem>();
            foreach (var uid in uids)
            {
                var clientUid = client.EntMan.GetEntity(server.EntMan.GetNetEntity(uid));
                sprites.SetVisible((clientUid, client.EntMan.GetComponent<SpriteComponent>(clientUid)), false);
            }
        });
        await pair.RunTicksSync(15);
        await client.WaitAssertion(() =>
        {
            for (var i = 0; i < uids.Count; i++)
            {
                var clientUid = client.EntMan.GetEntity(server.EntMan.GetNetEntity(uids[i]));
                Assert.That(client.EntMan.GetComponent<SpriteComponent>(clientUid).Visible, Is.False, $"{protos[i]} stays hidden.");
            }
        });

        // a burrow state update must not re-show a sprite something else hid
        var burrower = uids[2];
        var clientBurrower = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            var burrow = server.EntMan.GetComponent<XenoBurrowComponent>(burrower);
            burrow.Tunneling = true;
            server.EntMan.Dirty(burrower, burrow);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            clientBurrower = client.EntMan.GetEntity(server.EntMan.GetNetEntity(burrower));
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(clientBurrower).Visible, Is.False, "An unrelated burrow state update leaves the hidden burrower hidden.");
        });

        // burrowing hides a visible burrower and surfacing shows it again
        await client.WaitPost(() => client.EntMan.System<SpriteSystem>().SetVisible((clientBurrower, client.EntMan.GetComponent<SpriteComponent>(clientBurrower)), true));
        await server.WaitPost(() =>
        {
            var burrow = server.EntMan.GetComponent<XenoBurrowComponent>(burrower);
            burrow.Active = true;
            server.EntMan.Dirty(burrower, burrow);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => Assert.That(client.EntMan.GetComponent<SpriteComponent>(clientBurrower).Visible, Is.False, "A burrowed xeno is hidden."));
        await server.WaitPost(() =>
        {
            var burrow = server.EntMan.GetComponent<XenoBurrowComponent>(burrower);
            burrow.Active = false;
            server.EntMan.Dirty(burrower, burrow);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => Assert.That(client.EntMan.GetComponent<SpriteComponent>(clientBurrower).Visible, Is.True, "Surfacing shows the burrower the burrow hid."));

        await pair.CleanReturnAsync();
    }
}
