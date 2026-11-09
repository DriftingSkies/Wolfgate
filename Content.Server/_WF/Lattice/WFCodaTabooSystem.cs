using System.Text.RegularExpressions;
using Content.Server.Chat.Systems;
using Content.Shared._WF.Lattice;
using Content.Shared.Ghost;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The Coda won't be named. A living speaker who says the word stumbles on it, and feels it catch. The Choir, who are at
/// home in it, say it plainly.
/// </summary>
public sealed class WFCodaTabooSystem : EntitySystem
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
    }

    private void OnTransformSpeech(TransformSpeechEvent args)
    {
        if (!Word.IsMatch(args.Message)
            || HasComp<GhostComponent>(args.Sender)
            || HasComp<WFChoirMemberComponent>(args.Sender))
            return;

        args.Message = Word.Replace(args.Message, match => Stumble(match.Value));

        var now = _timing.CurTime;
        if (_nextPopup.TryGetValue(args.Sender, out var next) && now < next)
            return;

        _nextPopup[args.Sender] = now + PopupGap;
        _popup.PopupEntity(Loc.GetString("wf-coda-taboo-popup"), args.Sender, args.Sender, PopupType.SmallCaution);
    }

    /// <summary>The word as a tongue trips on it: the first sound caught on, and the rest lost.</summary>
    private string Stumble(string word)
    {
        var first = word[..1];
        return Loc.GetString($"wf-coda-taboo-stumble-{_random.Next(1, 5)}", ("first", first), ("lower", first.ToLowerInvariant()));
    }
}
