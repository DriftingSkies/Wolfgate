using System.Text.RegularExpressions;
using Content.Server.Chat.Systems;
using Content.Shared._WG.Lattice;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Lattice;

/// <summary>
/// The Coda won't be named. A living speaker who says the word stumbles on it, and feels it catch; a hand writing it
/// breaks off the same way. The Choir, who are at home in it, say and write it plainly.
/// </summary>
public sealed class WGCodaTabooSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly Regex Word = new(@"\bcoda\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly TimeSpan PopupGap = TimeSpan.FromSeconds(10);

    private readonly Dictionary<EntityUid, TimeSpan> _nextPopup = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);
        SubscribeLocalEvent<WGWrittenTextEvent>(OnWritten);
    }

    private void OnTransformSpeech(TransformSpeechEvent args)
    {
        if (!Word.IsMatch(args.Message) || Spared(args.Sender))
            return;

        args.Message = Word.Replace(args.Message, match => Stumble(match.Value));
        Warn(args.Sender, "wg-coda-taboo-popup");
    }

    private void OnWritten(ref WGWrittenTextEvent args)
    {
        if (!Word.IsMatch(args.Text) || Spared(args.Writer))
            return;

        args.Text = Word.Replace(args.Text, match => Stumble(match.Value));
        Warn(args.Writer, "wg-coda-taboo-popup-written");
    }

    /// <summary>Ghosts and the Choir name it freely.</summary>
    private bool Spared(EntityUid uid)
    {
        return HasComp<GhostComponent>(uid) || HasComp<WGChoirMemberComponent>(uid);
    }

    /// <summary>Tells someone the word caught, at most once every few seconds.</summary>
    private void Warn(EntityUid uid, LocId message)
    {
        if (!Exists(uid))
            return;

        var now = _timing.CurTime;
        if (_nextPopup.TryGetValue(uid, out var next) && now < next)
            return;

        _nextPopup[uid] = now + PopupGap;
        _popup.PopupEntity(Loc.GetString(message), uid, uid, PopupType.SmallCaution);
    }

    /// <summary>The word as a tongue trips on it: the first sound caught on, and the rest lost.</summary>
    private string Stumble(string word)
    {
        var first = word[..1];
        return Loc.GetString($"wg-coda-taboo-stumble-{_random.Next(1, 5)}", ("first", first), ("lower", first.ToLowerInvariant()));
    }
}
