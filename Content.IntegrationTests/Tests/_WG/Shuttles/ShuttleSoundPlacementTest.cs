#nullable enable annotations
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Tests.Interaction;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Effects;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.Shuttles;

/// <summary>
/// An unplaced source sits at the map origin, so a sound must never be turned up before it has been placed.
/// </summary>
public sealed class ShuttleSoundPlacementTest : InteractionTest
{
    private const string Up = "up";
    private const string Down = "down";
    private const string Placed = "placed";

    private static readonly SoundPathSpecifier Sound = new("/Audio/Effects/holy.ogg");

    [Test]
    public async Task ASoundOutOfRangeIsNeverTurnedUp()
    {
        var calls = await PlayAndRecord(new Vector2(500f, 0f));

        Assert.That(calls, Does.Contain(Down), "The audio pass never reached the sound.");
        Assert.That(calls, Does.Not.Contain(Up));
    }

    [Test]
    public async Task ASoundInRangeIsPlacedBeforeItIsTurnedUp()
    {
        var calls = await PlayAndRecord(new Vector2(2f, 0f));

        Assert.That(calls, Does.Contain(Up), "The audio pass never reached the sound.");
        Assert.That(calls.IndexOf(Placed), Is.GreaterThanOrEqualTo(0));
        Assert.That(calls.IndexOf(Placed), Is.LessThan(calls.IndexOf(Up)));
    }

    /// <summary>Starts a looping sound at an offset from the eye and records what the audio pass does to its source.</summary>
    private async Task<List<string>> PlayAndRecord(Vector2 offset)
    {
        var source = new RecordingSource();

        await Client.WaitPost(() =>
        {
            var eye = Client.ResolveDependency<IEyeManager>().CurrentEye.Position;
            var map = CEntMan.GetComponent<TransformComponent>(CPlayer).MapUid!.Value;
            var stream = CEntMan.System<SharedAudioSystem>().PlayStatic(Sound, Filter.Local(),
                new EntityCoordinates(map, eye.Position + offset), false, AudioParams.Default.WithLoop(true))!.Value;

            source.MaxDistance = stream.Component.MaxDistance;
            typeof(AudioComponent).GetField("Source", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(stream.Component, source);
            stream.Component.Started = false;
        });

        await RunTicks(30);
        return source.Calls;
    }

    private sealed class RecordingSource : IAudioSource
    {
        public readonly List<string> Calls = new();
        private float _gain;
        private Vector2 _position;

        public Vector2 Position
        {
            get => _position;
            set
            {
                _position = value;
                Calls.Add(Placed);
            }
        }

        public float Gain
        {
            get => _gain;
            set
            {
                _gain = value;
                Calls.Add(value > 0f ? Up : Down);
            }
        }

        public float Volume
        {
            get => SharedAudioSystem.GainToVolume(_gain);
            set => Gain = SharedAudioSystem.VolumeToGain(value);
        }

        public bool Playing { get; set; }
        public bool Looping { get; set; }
        public bool Global { get; set; }
        public float Pitch { get; set; }
        public float MaxDistance { get; set; }
        public float RolloffFactor { get; set; }
        public float ReferenceDistance { get; set; }
        public float Occlusion { get; set; }
        public float PlaybackPosition { get; set; }
        public Vector2 Velocity { get; set; }

        public void Pause() => Playing = false;
        public void StartPlaying() => Playing = true;
        public void StopPlaying() => Playing = false;
        public void Restart() => Playing = true;
        public void SetAuxiliary(IAuxiliaryAudio? audio) { }
        public void Dispose() { }
    }
}
