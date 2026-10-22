#nullable enable
using System.IO;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Client.Audio.Midi;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Instruments;

/// <summary>
/// The client's MIDI collection adds, reads, renames and removes files in user data (wizden#44469's tests).
/// </summary>
[TestFixture]
[TestOf(typeof(MidiFileCollectionManager))]
public sealed class MidiFileCollectionManagerTest
{
    private static readonly byte[] TestBytes = [1, 2, 3, 4, 5, 6];
    private static readonly ResPath TestFile = new("unit_test.midi");
    private static readonly ResPath Renamed = new("unit_test_renamed.midi");
    private static ResPath Dir => MidiFileCollectionManager.UserMidiDirectory;

    [Test]
    public async Task CollectionRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var res = pair.Client.ResolveDependency<IResourceManager>();
        var midis = pair.Client.ResolveDependency<MidiFileCollectionManager>();

        ResPath? added = null;
        ResPath? removed = null;
        var reset = false;
        Action<ResPath> onAdded = p => added = p;
        Action<ResPath> onRemoved = p => removed = p;
        Action onReset = () => reset = true;
        midis.MidiFileAdded += onAdded;
        midis.MidiFileRemoved += onRemoved;
        midis.MidiFilesReset += onReset;

        try
        {
            await pair.Client.WaitAssertion(() => midis.RemoveAllMidiFiles());

            Assert.That(await Add(pair, () => midis.AddMidiFile(TestFile, new MemoryStream(TestBytes))), Is.True);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(midis.GetMidiFiles(), Contains.Item(TestFile));
                Assert.That(res.UserData.ReadAllBytes(Dir / TestFile), Is.EqualTo(TestBytes));
                Assert.That(midis.GetMidiData(TestFile), Is.EqualTo(TestBytes));
                Assert.That(added, Is.EqualTo(TestFile));
            });

            // Rename raises removed + added and moves the file.
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(midis.RenameMidiFile(TestFile, Renamed), Is.True);
                Assert.That(res.UserData.Exists(Dir / Renamed), Is.True);
                Assert.That(res.UserData.Exists(Dir / TestFile), Is.False);
                Assert.That(removed, Is.EqualTo(TestFile));
                Assert.That(added, Is.EqualTo(Renamed));
            });

            // Remove one.
            await pair.Client.WaitAssertion(() =>
            {
                midis.RemoveMidiFile(Renamed);
                Assert.That(res.UserData.Exists(Dir / Renamed), Is.False);
                Assert.That(midis.GetMidiFiles(), Is.Empty);
                Assert.That(removed, Is.EqualTo(Renamed));
            });

            // Files copied in from outside the game show up after a reload.
            await pair.Client.WaitAssertion(() =>
            {
                res.UserData.WriteAllBytes(Dir / TestFile, TestBytes);
                midis.ReloadLibrary();
                Assert.That(midis.GetMidiFiles(), Contains.Item(TestFile));
            });

            // Clear all.
            await Add(pair, () => midis.AddMidiFile(new ResPath("1_unit_test.midi"), TestBytes));
            await Add(pair, () => midis.AddMidiFile(new ResPath("2_unit_test.midi"), TestBytes));
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(midis.GetMidiFiles().Count(), Is.EqualTo(3));
                reset = false;
                midis.RemoveAllMidiFiles();
                Assert.That(midis.GetMidiFiles(), Is.Empty);
                Assert.That(reset, Is.True);
            });
        }
        finally
        {
            midis.MidiFileAdded -= onAdded;
            midis.MidiFileRemoved -= onRemoved;
            midis.MidiFilesReset -= onReset;
            await pair.Client.WaitPost(() => midis.RemoveAllMidiFiles());
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Starts an add on the client thread, which user data needs, then ticks the client until it finishes: its
    /// continuations post to the client loop, so blocking that thread on them deadlocks.
    /// </summary>
    private static async Task<bool> Add(TestPair pair, Func<Task<bool>> add)
    {
        Task<bool>? task = null;
        await pair.Client.WaitPost(() => task = add());
        for (var i = 0; i < 600 && !task!.IsCompleted; i++)
            await pair.Client.WaitRunTicks(1);

        Assert.That(task!.IsCompleted, Is.True, "The MIDI add never finished.");
        return await task;
    }
}
