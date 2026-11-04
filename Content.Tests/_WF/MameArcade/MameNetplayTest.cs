using System;
using System.Collections.Generic;
using Content.Client._WF.MameArcade;
using NUnit.Framework;

namespace Content.Tests._WF.MameArcade;

/// <summary>
/// Two clients exchanging inputs through a laggy link emulate the same frames with the same controls, and settle on
/// about the same small delay however the link and their start times differ.
/// </summary>
[TestFixture]
[TestOf(typeof(MameNetplay))]
public sealed class MameNetplayTest
{
    private const float Tick = 1 / 60f;

    [Test]
    public void BothSidesSeeTheSameInputs()
    {
        var random = new Random(1234);
        var match = new Match(4, 4, _ => random.Next(1, 4));
        match.Run(900, adapt: false);

        match.AssertSameFrames();
        Assert.That(match.HostFrames, Has.Count.GreaterThan(600));
        Assert.That(match.HostFrames[0], Is.EqualTo((MameControls.None, MameControls.None)), "The first frames have no input yet");
    }

    [Test]
    public void DelayGrowsUntilASlowLinkStopsStalling()
    {
        // 15 ticks each way, far more than the starting delay.
        var match = new Match(2, 2, _ => 15);
        match.Run(60 * 40);

        match.AssertSameFrames();
        match.AssertFairAndSmooth(15, 22);
    }

    [Test]
    public void LateStarterGetsTheSameDelay()
    {
        // The partner finishing loading a second later used to leave one player waiting every frame, with all the lag.
        var match = new Match(4, 4, _ => 6) { GuestStart = 60 };
        match.Run(60 * 40);

        match.AssertSameFrames();
        match.AssertFairAndSmooth(6, 12);
    }

    [Test]
    public void FarFutureInputsAreIgnored()
    {
        var netplay = new MameNetplay(0, 2);
        netplay.ReceiveInputs(MameNetplay.MaxAhead + 1, new ushort[] { 1 }, MameNetplay.UnknownMargin, 2);
        netplay.ReceiveInputs(int.MaxValue - 1, new ushort[] { 1, 1, 1 }, MameNetplay.UnknownMargin, 2);

        // Only the two delay frames are known, so the third waits for the partner.
        Assert.That(netplay.TryAdvance(MameControls.None, out _, out _), Is.True);
        Assert.That(netplay.TryAdvance(MameControls.None, out _, out _), Is.True);
        Assert.That(netplay.TryAdvance(MameControls.None, out _, out _), Is.False);
        Assert.That(netplay.AddRemoteChecksum(MameNetplay.ChecksumInterval * 1000, 1), Is.False);
    }

    [Test]
    public void DelayComesDownWhenTheLinkIsFast()
    {
        var match = new Match(20, 20, _ => 2);
        match.Run(60 * 60);

        match.AssertSameFrames();
        match.AssertFairAndSmooth(2, 8);
    }

    [Test]
    public void StallsUntilThePartnersInputArrives()
    {
        var netplay = new MameNetplay(0, 2);

        Assert.That(netplay.TryAdvance(MameControls.Coin, out _, out _), Is.True);
        Assert.That(netplay.TryAdvance(MameControls.Coin, out _, out _), Is.True);
        Assert.That(netplay.TryAdvance(MameControls.Start, out _, out _), Is.False, "Frame 2 needs player 2's input");

        netplay.ReceiveInputs(2, new ushort[] { (ushort) MameControls.Button1 }, MameNetplay.UnknownMargin, 2);
        Assert.That(netplay.TryAdvance(MameControls.Start, out var one, out var two), Is.True);
        Assert.That(one, Is.EqualTo(MameControls.Coin), "Frame 2 uses the key held two frames earlier");
        Assert.That(two, Is.EqualTo(MameControls.Button1));
    }

    [Test]
    public void PlaysOnAloneAfterThePartnerLeaves()
    {
        var netplay = new MameNetplay(1, 2);
        netplay.TryAdvance(MameControls.Up, out _, out _);
        netplay.TryAdvance(MameControls.Up, out _, out _);
        Assert.That(netplay.TryAdvance(MameControls.Up, out _, out _), Is.False);

        netplay.PartnerLeft();
        Assert.That(netplay.TryAdvance(MameControls.Up, out var one, out var two), Is.True);
        Assert.That(one, Is.EqualTo(MameControls.None));
        Assert.That(two, Is.EqualTo(MameControls.Up), "Seat 1 is player 2");
    }

    [Test]
    public void ChecksumMismatchMeansDesync()
    {
        var netplay = new MameNetplay(0, 2);
        Assert.That(netplay.AddLocalChecksum(120, 0xabc), Is.False);
        Assert.That(netplay.AddRemoteChecksum(120, 0xabc), Is.False);
        Assert.That(netplay.Desynced, Is.False);

        Assert.That(netplay.AddRemoteChecksum(240, 0x123), Is.False);
        Assert.That(netplay.AddLocalChecksum(240, 0x124), Is.True);
        Assert.That(netplay.Desynced, Is.True);
    }

    /// <summary>Two clients at 60 frames a second, as the session paces them, joined by an in-order link.</summary>
    private sealed class Match
    {
        public readonly MameNetplay Host;
        public readonly MameNetplay Guest;
        public readonly List<(MameControls, MameControls)> HostFrames = new();
        public readonly List<(MameControls, MameControls)> GuestFrames = new();

        /// <summary>Ticks before the guest starts running, as if it took longer to load.</summary>
        public int GuestStart;

        private readonly Func<int, int> _latency;
        private readonly Queue<(int Due, int First, ushort[] Inputs, int Margin, int Delay)> _toGuest = new();
        private readonly Queue<(int Due, int First, ushort[] Inputs, int Margin, int Delay)> _toHost = new();
        private readonly Random _keys = new(42);
        private int _hostDue;
        private int _guestDue;
        private int _hostFramesBeforeEnd;
        private int _guestFramesBeforeEnd;

        public Match(int hostDelay, int guestDelay, Func<int, int> latency)
        {
            Host = new MameNetplay(0, hostDelay);
            Guest = new MameNetplay(1, guestDelay);
            _latency = latency;
        }

        public void Run(int ticks, bool adapt = true)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                if (tick == ticks - 600)
                {
                    _hostFramesBeforeEnd = HostFrames.Count;
                    _guestFramesBeforeEnd = GuestFrames.Count;
                }

                Step(Host, ref _hostDue, HostFrames, adapt);
                if (tick >= GuestStart)
                    Step(Guest, ref _guestDue, GuestFrames, adapt);

                Send(Host, _toGuest, tick + _latency(tick));
                Send(Guest, _toHost, tick + _latency(tick));
                Deliver(_toGuest, Guest, tick);
                Deliver(_toHost, Host, tick);
            }
        }

        public void AssertSameFrames()
        {
            var shared = Math.Min(HostFrames.Count, GuestFrames.Count);
            Assert.That(shared, Is.GreaterThan(0));
            for (var frame = 0; frame < shared; frame++)
            {
                Assert.That(GuestFrames[frame], Is.EqualTo(HostFrames[frame]), $"Frame {frame} differs");
            }
        }

        /// <summary>Both delays in range and within two frames of each other, and the last 10 seconds near 60 fps.</summary>
        public void AssertFairAndSmooth(int lowest, int highest)
        {
            var state = $"delays {Host.Delay}/{Guest.Delay}, margins {Host.Margin}/{Guest.Margin}";
            Assert.That(Host.Delay, Is.InRange(lowest, highest), $"Host delay: {state}");
            Assert.That(Guest.Delay, Is.InRange(lowest, highest), $"Guest delay: {state}");
            Assert.That(Math.Abs(Host.Delay - Guest.Delay), Is.LessThanOrEqualTo(2), $"Both players get about the same lag: {state}");
            Assert.That(HostFrames.Count - _hostFramesBeforeEnd, Is.GreaterThan(570), "Host keeps up");
            Assert.That(GuestFrames.Count - _guestFramesBeforeEnd, Is.GreaterThan(570), "Guest keeps up");
        }

        /// <summary>One frame comes due each tick; frames owed from waiting catch up, four at most.</summary>
        private void Step(MameNetplay netplay, ref int due, List<(MameControls, MameControls)> frames, bool adapt)
        {
            due = Math.Min(due + 1, 4);
            var held = (MameControls) _keys.Next(0, 0xfff);
            while (due > 0)
            {
                if (netplay.TakeHold())
                {
                    due--;
                    continue;
                }

                if (!netplay.TryAdvance(held, out var one, out var two))
                    break;

                frames.Add((one, two));
                due--;
            }

            if (adapt)
                netplay.Adapt(Tick);
        }

        private static void Send(MameNetplay from, Queue<(int, int, ushort[], int, int)> link, int due)
        {
            if (from.TakeOutbox(out var first, out var inputs))
                link.Enqueue((due, first, inputs, from.Margin, from.Delay));
        }

        private static void Deliver(Queue<(int Due, int First, ushort[] Inputs, int Margin, int Delay)> link, MameNetplay to, int tick)
        {
            // In order: a message never overtakes an earlier one.
            while (link.TryPeek(out var next) && next.Due <= tick)
            {
                link.Dequeue();
                to.ReceiveInputs(next.First, next.Inputs, next.Margin, next.Delay);
            }
        }
    }
}
