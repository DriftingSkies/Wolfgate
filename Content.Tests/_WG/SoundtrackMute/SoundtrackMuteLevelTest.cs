using Content.Client._WG.SoundtrackMute;
using NUnit.Framework;

namespace Content.Tests._WG.SoundtrackMute;

/// <summary>The level fades down while audible, holds through short quiet, and fades back up after the hold.</summary>
[TestFixture]
[TestOf(typeof(SoundtrackMuteLevel))]
public sealed class SoundtrackMuteLevelTest
{
    private const float Step = 1f / 60f;

    private static void Run(SoundtrackMuteLevel level, bool audible, float seconds)
    {
        for (var t = 0f; t < seconds; t += Step)
        {
            level.Update(audible, Step);
        }
    }

    [Test]
    public void FadesOutWhileAudible()
    {
        var level = new SoundtrackMuteLevel();
        Assert.That(level.Level, Is.EqualTo(1f));

        Run(level, true, SoundtrackMuteLevel.FadeSeconds / 2f);
        Assert.That(level.Level, Is.InRange(0.4f, 0.6f));

        Run(level, true, SoundtrackMuteLevel.FadeSeconds);
        Assert.That(level.Level, Is.Zero);
    }

    [Test]
    public void HoldsThroughShortQuiet()
    {
        var level = new SoundtrackMuteLevel();
        Run(level, true, SoundtrackMuteLevel.FadeSeconds);

        Run(level, false, SoundtrackMuteLevel.HoldSeconds / 2f);
        Assert.That(level.Level, Is.Zero);
    }

    [Test]
    public void FadesInAfterHold()
    {
        var level = new SoundtrackMuteLevel();
        Run(level, true, SoundtrackMuteLevel.FadeSeconds);

        Run(level, false, SoundtrackMuteLevel.HoldSeconds + SoundtrackMuteLevel.FadeSeconds / 2f);
        Assert.That(level.Level, Is.InRange(0.4f, 0.6f));

        Run(level, false, SoundtrackMuteLevel.FadeSeconds);
        Assert.That(level.Level, Is.EqualTo(1f));
    }

    [Test]
    public void AudibleAgainMidFadeTurnsBackDown()
    {
        var level = new SoundtrackMuteLevel();
        Run(level, true, SoundtrackMuteLevel.FadeSeconds);
        Run(level, false, SoundtrackMuteLevel.HoldSeconds + SoundtrackMuteLevel.FadeSeconds / 2f);
        var partWayUp = level.Level;

        Run(level, true, Step);
        Assert.That(level.Level, Is.LessThan(partWayUp));

        Run(level, false, SoundtrackMuteLevel.HoldSeconds / 2f);
        Assert.That(level.Level, Is.LessThan(partWayUp), "The hold must restart after sound returns.");
    }

    [Test]
    public void ResetRestoresFull()
    {
        var level = new SoundtrackMuteLevel();
        Run(level, true, SoundtrackMuteLevel.FadeSeconds);

        level.Reset();
        Assert.That(level.Level, Is.EqualTo(1f));
    }
}
