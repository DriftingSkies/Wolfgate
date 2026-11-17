using System;
using System.Linq;
using Content.Client._WG.Voice;
using Content.Shared._WG.Voice;
using NUnit.Framework;

namespace Content.Tests._WG.Voice;

/// <summary>Rendering into reused buffers gives the same line as fresh ones, and the longest line fits them.</summary>
[TestFixture]
public sealed class SpeechVoiceSynthTest
{
    private const string Line = "the quick brown fox jumps over the lazy dog, and then? it naps!";

    [Test]
    public void DirtyBuffersRenderTheSameLine([Values] SpeechVoiceFilter filter, [Values] bool radio)
    {
        var voice = new SpeechVoice { Filter = filter };

        var fresh = new SpeechVoiceBuffers();
        var length = new SpeechVoiceSynth().Render(Line, voice, radio, fresh);

        var dirty = new SpeechVoiceBuffers();
        Array.Fill(dirty.Samples, 1f);
        Array.Fill(dirty.Dry, 1f);
        Array.Fill(dirty.Pcm, short.MaxValue);
        var dirtyLength = new SpeechVoiceSynth().Render(Line, voice, radio, dirty);

        Assert.That(length, Is.GreaterThan(0));
        Assert.That(dirtyLength, Is.EqualTo(length));
        Assert.That(dirty.Pcm.Take(length), Is.EqualTo(fresh.Pcm.Take(length)));
    }

    [Test]
    public void LongestLineFitsTheBuffers([Values] SpeechVoiceFilter filter)
    {
        var voice = new SpeechVoice
        {
            Filter = filter,
            Speed = SpeechVoice.MaxSpeed,
            Pitch = SpeechVoice.MinPitch,
        };
        var text = string.Concat(Enumerable.Repeat("long words. ", 40));

        var length = new SpeechVoiceSynth().Render(text, voice, true, new SpeechVoiceBuffers());

        Assert.That(length, Is.InRange(1, SpeechVoiceSynth.MaxSamples));
    }

    [Test]
    public void GapsBetweenWordsAreSilent([Values] bool radio)
    {
        var buffers = new SpeechVoiceBuffers();
        var voice = new SpeechVoice();
        var slot = (int) (voice.Speed * 0.5f * SpeechVoiceSynth.SampleRate);

        new SpeechVoiceSynth().Render("hello there", voice, radio, buffers);

        // The last quarter of the one-letter gap after "hello".
        var gapEnd = 6 * slot;
        var loudest = buffers.Pcm.Skip(gapEnd - slot / 4).Take(slot / 4).Max(s => Math.Abs((int) s));
        Assert.That(loudest, Is.LessThan(short.MaxValue / 1000), "no static may sit between words");
    }

    [Test]
    public void WordsEaseOutInsteadOfCutting([Values] bool radio)
    {
        var buffers = new SpeechVoiceBuffers();
        var voice = new SpeechVoice();
        var slot = (int) (voice.Speed * 0.5f * SpeechVoiceSynth.SampleRate);

        new SpeechVoiceSynth().Render("hello there", voice, radio, buffers);

        // At full level until the last 12 ms, nearly gone by the last millisecond.
        var wordEnd = 5 * slot;
        var body = buffers.Pcm.Skip(wordEnd - slot).Take(slot / 2).Max(s => Math.Abs((int) s));
        var lastMs = buffers.Pcm.Skip(wordEnd - SpeechVoiceSynth.SampleRate / 1000)
            .Take(SpeechVoiceSynth.SampleRate / 1000)
            .Max(s => Math.Abs((int) s));
        Assert.That(lastMs, Is.LessThan(body / 10));
    }

    [Test]
    public void LizardHissTailIsStretched()
    {
        var voice = new SpeechVoice { Filter = SpeechVoiceFilter.Lizard };
        var slot = (int) (voice.Speed * 1.15f * 0.5f * SpeechVoiceSynth.SampleRate);

        var length = new SpeechVoiceSynth().Render("a", voice, false, new SpeechVoiceBuffers());

        Assert.That(length, Is.EqualTo(slot + (int) (slot * 0.8f * 1.8f)));
    }

    [Test]
    public void NoLettersRenderNothing()
    {
        Assert.That(new SpeechVoiceSynth().Render("... !?", new SpeechVoice(), false, new SpeechVoiceBuffers()),
            Is.Zero);
    }
}
