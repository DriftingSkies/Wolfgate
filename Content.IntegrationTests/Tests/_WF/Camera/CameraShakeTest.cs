#nullable enable
using System.Numerics;
using Content.Shared.Camera;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Camera;

/// <summary>A camera kick of any size moves the view only a short way.</summary>
[TestFixture]
public sealed class CameraShakeTest
{
    [Test]
    public async Task HardKickStaysClose()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;

        await client.WaitAssertion(() =>
        {
            var uid = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var recoil = entMan.AddComponent<CameraRecoilComponent>(uid);
            var system = entMan.System<SharedCameraRecoilSystem>();

            system.KickCamera(uid, new Vector2(0.1f, 0f), recoil);
            Assert.That(recoil.CurrentKick.X, Is.EqualTo(0.1f).Within(0.001f), "A light kick passes through.");

            system.KickCamera(uid, new Vector2(50f, 0f), recoil);
            system.KickCamera(uid, new Vector2(0f, -50f), recoil);
            Assert.That(recoil.CurrentKick.Length(), Is.LessThanOrEqualTo(0.3001f), "A hard kick is capped.");

            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }
}
