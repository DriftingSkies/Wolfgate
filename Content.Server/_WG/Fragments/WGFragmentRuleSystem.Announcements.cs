using Content.Server._WG.PlanetComms;
using Content.Server.Chat.Managers;
using Content.Shared._WG.Lattice;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._WG.Fragments;

/// <summary>The fragment's announcements: two-tone, corrupted while its heart beats, and followed by its whisper.</summary>
public sealed partial class WGFragmentRuleSystem
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private WGPlanetCommsSystem _planetComms = default!;

    /// <summary>Whispers waiting to follow their announcement; dropped when the round restarts.</summary>
    private readonly List<(TimeSpan At, string Plain, string Wrapped)> _whispers = new();

    private void InitializeAnnouncements()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _whispers.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        for (var i = _whispers.Count - 1; i >= 0; i--)
        {
            if (Timing.CurTime < _whispers[i].At)
                continue;
            Send(_whispers[i].Plain, _whispers[i].Wrapped, null);
            _whispers.RemoveAt(i);
        }
    }

    protected override void Added(EntityUid uid, WGFragmentRuleComponent comp, GameRuleComponent gameRule, GameRuleAddedEvent args)
    {
        base.Added(uid, comp, gameRule, args);
        ChooseSize(comp);
        Announce(comp, comp.Sighted, null);
    }

    /// <summary>
    /// Sends a line with the fragment's position to the nearest 250 m, then queues its whisper if it has one. A
    /// <paramref name="calm"/> line, sent after the heart broke, takes the broken line's colours with no corruption.
    /// </summary>
    private void Announce(WGFragmentRuleComponent comp, WGFragmentLine line, MapCoordinates? at, bool joint = false,
        bool calm = false)
    {
        // The position goes into the text as one value, so it can be found and kept whatever the wording around it.
        var coords = at is { } site
            ? Loc.GetString("wg-fragment-coords",
                ("x", (int) MathF.Round(site.X / 250f) * 250),
                ("y", (int) MathF.Round(site.Y / 250f) * 250))
            : null;

        var text = Loc.GetString(joint && line.JointText is { } jointText ? jointText : line.Text, ("coords", coords ?? ""));
        var words = Loc.GetString(comp.HiddenWords).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var random = new System.Random(RobustRandom.Next());
        var body = WGFragmentText.Build(text, coords, calm ? 0f : line.Corruption, calm ? 0 : line.HiddenWords,
            calm ? 0 : line.Flickers, words, comp.FlickerRate, random);
        var sender = WGLatticeMath.Garble(Loc.GetString("wg-fragment-announcer"), calm ? 0f : line.SenderCorruption,
            random.Next());

        var wrapped = Loc.GetString("wg-fragment-announcement-wrap",
            ("senderColor", (calm ? comp.Broken.SenderColor : line.SenderColor).ToHexNoAlpha()),
            ("sender", FormattedMessage.EscapeText(sender)),
            ("textColor", (calm ? comp.Broken.TextColor : line.TextColor).ToHexNoAlpha()),
            ("message", body.Markup));
        Send(text, wrapped, line.Sound);

        if (calm || line.Voice.Count == 0)
            return;

        var whisper = WGFragmentText.Whisper(Loc.GetString(RobustRandom.Pick(line.Voice)),
            comp.FlickerRate + random.Next(WGFragmentText.RateSpread));
        var voice = Loc.GetString("wg-fragment-voice-wrap",
            ("color", comp.VoiceColor.ToHexNoAlpha()),
            ("message", whisper.Markup));
        _whispers.Add((Timing.CurTime + line.VoiceDelay, whisper.Plain, voice));
    }

    /// <summary>
    /// Sends a chat line to every player in the round who can hear it: a world with no mass transceiver up misses it,
    /// as it misses any other announcement. <paramref name="plain"/> is the clean text, for the chat log and admin log.
    /// </summary>
    private void Send(string plain, string wrapped, SoundSpecifier? sound)
    {
        var filter = Filter.Empty().AddWhere(GameTicker.UserHasJoinedGame);
        _planetComms.TryNarrow(filter, null, out var reach);

        _chatManager.ChatMessageToManyFiltered(reach, ChatChannel.Radio, plain, wrapped, default, false, true, null);
        if (sound != null)
            Audio.PlayGlobal(Audio.ResolveSound(sound), reach, true, sound.Params.WithVolume(sound.Params.Volume - 2f));

        AdminLogManager.Add(LogType.Chat, LogImpact.Low, $"Monolith fragment announcement: {plain}");
    }
}
