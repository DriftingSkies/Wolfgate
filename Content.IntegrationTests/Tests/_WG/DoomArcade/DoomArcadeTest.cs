#nullable enable
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Content.Client._WG.DoomArcade;
using Content.Client._WG.DoomArcade.Engine;
using Content.Client._WG.DoomArcade.MeltySynth;
using Content.Shared._WG.DoomArcade;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._WG.DoomArcade;

/// <summary>
/// The ported engine loads Freedoom, draws a level and responds to input; audio renders; the cabinet opens its window.
/// </summary>
[TestOf(typeof(DoomArcadeSession))]
public sealed class DoomArcadeTest
{
    private const float Tic = 1f / 35f;
    private const string WadName = "freedoom1.wad";
    private const string WadPath = "/GameData/_WG/DoomArcade/freedoom1.wad";
    private const string Phase2Name = "freedoom2.wad";
    private const string Phase2Path = "/GameData/_WG/DoomArcade/freedoom2.wad";
    private const string SoundFontPath = "/Textures/_WG/DoomArcade/TimGM6mb.sf2";

    [Test]
    public async Task LevelRendersAndPlayerWalks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var resources = client.ResolveDependency<IResourceManager>();
            using var session = new DoomArcadeSession(WadName, Read(resources, WadPath), null, null, "-warp", "1", "1");

            Step(session, 35);
            session.Render();

            Assert.That(session.Doom.State, Is.EqualTo(DoomState.Game));
            Assert.That(session.Pixels.Distinct().Count(), Is.GreaterThan(32), "The frame looks blank");
            Dump(session, "spawn");

            var mobj = session.Doom.Game.World.ConsolePlayer.Mobj;
            var start = (mobj.X.Data, mobj.Y.Data);

            session.SetKey(DoomKey.W, true);
            Step(session, 35);
            session.SetKey(DoomKey.W, false);
            session.Render();

            Assert.That((mobj.X.Data, mobj.Y.Data), Is.Not.EqualTo(start), "Holding forward did not move the player");
            Dump(session, "walked");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Phase2LevelRendersAndPlayerWalks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var resources = client.ResolveDependency<IResourceManager>();
            using var session = new DoomArcadeSession(Phase2Name, Read(resources, Phase2Path), null, null, "-warp", "1");

            var finished = new List<DoomLevelResult>();
            session.LevelCompleted += finished.Add;

            Step(session, 120);
            session.Render();

            Assert.That(session.Doom.State, Is.EqualTo(DoomState.Game));
            Assert.That(session.Doom.Options.Episode * 10 + session.Doom.Options.Map, Is.EqualTo(11), "MAP01 is level 11 on the board");
            Assert.That(session.Pixels.Distinct().Count(), Is.GreaterThan(32), "The frame looks blank");
            Dump(session, "phase2");

            var mobj = session.Doom.Game.World.ConsolePlayer.Mobj;
            var start = (mobj.X.Data, mobj.Y.Data);

            session.SetKey(DoomKey.W, true);
            Step(session, 35);
            session.SetKey(DoomKey.W, false);

            Assert.That((mobj.X.Data, mobj.Y.Data), Is.Not.EqualTo(start), "Holding forward did not move the player");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Phase2TitleLoopPlaysItsDemos()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var resources = client.ResolveDependency<IResourceManager>();
            using var session = new DoomArcadeSession(Phase2Name, Read(resources, Phase2Path), null, null);

            for (var i = 0; i < 35 * 60; i++)
            {
                session.Update(Tic, out _);
                if (i % 35 == 0)
                    session.Render();
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EachGameKeepsItsOwnSaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var resources = client.ResolveDependency<IResourceManager>();
            var saves = new VirtualWritableDirProvider();

            using (var phase1 = new DoomArcadeSession(WadName, Read(resources, WadPath), null, saves, "-warp", "1", "1"))
            {
                Step(phase1, 120);
                Assert.That(phase1.Doom.SaveGame(0, "PHASE ONE"));
                Step(phase1, 120);
            }

            using var phase2 = new DoomArcadeSession(Phase2Name, Read(resources, Phase2Path), null, saves, "-warp", "1");
            Assert.That(phase2.Doom.Menu.SaveSlots[0], Is.Null, "Phase 2 must not list Phase 1's saves");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SaveGamesOutliveTheSession()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var resources = client.ResolveDependency<IResourceManager>();
            var saves = new VirtualWritableDirProvider();

            using (var session = new DoomArcadeSession(WadName, Read(resources, WadPath), null, saves, "-warp", "1", "1"))
            {
                Step(session, 35);
                Assert.That(session.Doom.SaveGame(0, "ROUND ONE"), "Saving should work in a level");

                // The game doesn't tick until the level's screen wipe ends.
                Step(session, 120);
            }

            using var next = new DoomArcadeSession(WadName, Read(resources, WadPath), null, saves, "-warp", "1", "1");
            Assert.That(next.Doom.Menu.SaveSlots[0], Does.StartWith("ROUND ONE"), "The next round should see the save");

            Step(next, 120);
            next.Doom.LoadGame(0);
            Step(next, 120);
            Assert.That(next.Doom.State, Is.EqualTo(DoomState.Game));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LevelMusicStartsWithSoundEnabled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var audio = new DoomArcadeAudio(
                client.ResolveDependency<IAudioManager>(),
                client.ResolveDependency<IConfigurationManager>(),
                Read(client.ResolveDependency<IResourceManager>(), SoundFontPath).ToArray(),
                client.ResolveDependency<ILogManager>().GetSawmill("doom.arcade"));

            using var session = new DoomArcadeSession(WadName, Read(client.ResolveDependency<IResourceManager>(), WadPath), audio, null, "-warp", "1", "1");

            var timer = Stopwatch.StartNew();
            while (!session.Music!.Started && timer.Elapsed.TotalSeconds < 120)
            {
                Step(session, 1);
                session.Render();
                Thread.Sleep(10);
            }

            TestContext.Progress.WriteLine($"Doom music started after {timer.Elapsed.TotalSeconds:F1} s");
            Assert.That(session.Music.Started, "The level's music never started");

            // Firing plays a sound on the same path the real audio device uses.
            session.SetKey(DoomKey.LControl, true);
            Step(session, 35);
            session.SetKey(DoomKey.LControl, false);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MusicTrackRendersSound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var resources = client.ResolveDependency<IResourceManager>();
            var soundFont = new SoundFont(Read(resources, SoundFontPath));
            using var session = new DoomArcadeSession(WadName, Read(resources, WadPath), null, null);
            var midi = session.Content.Wad.ReadLump("D_E1M1");

            var timer = Stopwatch.StartNew();
            var samples = DoomArcadeMusic.Render(soundFont, midi, CancellationToken.None);
            var seconds = samples!.Length / 2.0 / DoomArcadeMusic.SampleRate;
            TestContext.Progress.WriteLine($"Doom track: {seconds:F1} s rendered in {timer.Elapsed.TotalSeconds:F1} s");

            Assert.That(seconds, Is.GreaterThan(10));
            Assert.That(samples.Any(sample => sample != 0), "The track rendered silent");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(DoomArcadeMusic.Render(soundFont, midi, cancelled.Token), Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CabinetOpensWindow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
            var cabinet = server.EntMan.SpawnEntity("WGDoomArcade", map.GridCoords);
            server.System<SharedUserInterfaceSystem>().OpenUi(cabinet, DoomArcadeUiKey.Key, player);
        });

        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var window = client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                .OfType<DoomArcadeWindow>()
                .SingleOrDefault();

            Assert.That(window, Is.Not.Null, "The cabinet did not open its window");
            window!.Close();
        });

        await pair.CleanReturnAsync();
    }

    private static MemoryStream Read(IResourceManager resources, string path)
    {
        var memory = new MemoryStream();
        using (var file = resources.ContentFileRead(new ResPath(path)))
        {
            file.CopyTo(memory);
        }

        memory.Position = 0;
        return memory;
    }

    private static void Step(DoomArcadeSession session, int tics)
    {
        for (var i = 0; i < tics; i++)
        {
            session.Update(Tic, out _);
        }
    }

    // Set DOOM_FRAME_DIR to look at the frames this test renders.
    private static void Dump(DoomArcadeSession session, string name)
    {
        if (Environment.GetEnvironmentVariable("DOOM_FRAME_DIR") is not { } dir)
            return;

        using var image = Image.LoadPixelData<Rgba32>(session.Pixels, session.Width, session.Height);
        image.SaveAsPng(Path.Combine(dir, $"{name}.png"));
    }
}
