using Content.Shared._WG.Performance;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using ClientLightSound = Content.Client._WG.Performance.WGLightSoundSystem;
using ServerLightSound = Content.Server._WG.Performance.WGLightSoundSystem;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks a light sound reaches a client in range as a message, with no sound entity on the server.</summary>
[TestFixture]
[TestOf(typeof(ServerLightSound))]
public sealed class WGLightSoundTest
{
    private static readonly SoundPathSpecifier Sound = new("/Audio/Weapons/Guns/Gunshots/pistol.ogg");

    [Test]
    public async Task ASoundIsSentWithoutAnEntity()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var light = server.System<ServerLightSound>();
        var heard = pair.Client.System<ClientLightSound>();
        var map = await pair.CreateTestMap();
        var session = pair.Player!;

        EntityUid listener = default;
        EntityUid source = default;
        await server.WaitPost(() =>
        {
            listener = entMan.SpawnEntity("MobHuman", map.GridCoords);
            source = entMan.SpawnEntity("CrowbarRed", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(session, listener);
        });
        await pair.RunTicksSync(10);

        try
        {
            var played = heard.Played;
            await server.WaitAssertion(() =>
            {
                var sounds = entMan.Count<AudioComponent>();
                var sent = light.Sent;
                light.PlayPvs(Sound, source);
                Assert.That(light.Sent, Is.EqualTo(sent + 1));

                // The one who predicts it is not sent it, and with nobody else in range nothing goes out.
                light.PlayPredicted(Sound, source, listener);
                Assert.That(light.Sent, Is.EqualTo(sent + 1));
                Assert.That(entMan.Count<AudioComponent>(), Is.EqualTo(sounds), "A light sound made a sound entity.");
            });

            await pair.RunTicksSync(10);
            Assert.That(heard.Played, Is.EqualTo(played + 1), "The client in range did not play the sound.");

            await server.WaitAssertion(() =>
            {
                server.CfgMan.SetCVar(WGLightSoundCVars.Enabled, false);
                var sounds = entMan.Count<AudioComponent>();
                var sent = light.Sent;
                light.PlayPvs(Sound, source);
                Assert.That(light.Sent, Is.EqualTo(sent));
                Assert.That(entMan.Count<AudioComponent>(), Is.EqualTo(sounds + 1), "Switched off, it should play the upstream way.");
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WGLightSoundCVars.Enabled, true);
                server.PlayerMan.SetAttachedEntity(session, null);
                entMan.DeleteEntity(listener);
                entMan.DeleteEntity(source);
            });
        }

        await pair.CleanReturnAsync();
    }
}
