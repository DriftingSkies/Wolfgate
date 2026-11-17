using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._WG.TradeGoods;
using Content.Shared.GameTicking;
using Robust.Shared.Random;

namespace Content.Server._WG.TradeGoods.Systems;

/// <summary>
/// The contraband drop-point: one open station at a time buys smuggling crates, and the rest refuse them. It moves every
/// <c>contrabandDropInterval</c>; the Underground's companies hear where at once and see it in the Market app, and
/// security is tipped off <c>contrabandDropLeak</c> later.
/// </summary>
public sealed partial class WGTradeContrabandSystem
{
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WGTradeSectorSystem _sector = default!;

    private static readonly TimeSpan DropCheckInterval = TimeSpan.FromSeconds(5);

    private TimeSpan _nextDropCheck;
    private TimeSpan _dropLeaks;

    /// <summary>The one open station buying contraband now, or null when there is none to pick or the rotation is off.</summary>
    public EntityUid? DropPoint { get; private set; }

    /// <summary>When the drop moves on.</summary>
    public TimeSpan DropEnds { get; private set; }

    /// <summary>Whether security has been tipped off about the current drop.</summary>
    public bool DropLeaked { get; private set; }

    /// <summary>Whether contraband sells at the drop alone right now.</summary>
    public bool DropActive => DropPoint is { } drop && !TerminatingOrDeleted(drop);

    /// <summary>The sector-wide market profile, which sets the drop.</summary>
    private WGTradeMarketPrototype Profile => _market.Profile(null);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound || _timing.CurTime < _nextDropCheck)
            return;

        _nextDropCheck = _timing.CurTime + DropCheckInterval;
        TickDrop();
    }

    /// <summary>
    /// Moves the drop when it is due, its station is gone or the rotation was turned off, and tips off security once the
    /// leak delay has passed.
    /// </summary>
    public void TickDrop()
    {
        if (!DropActive || _timing.CurTime >= DropEnds || Profile.ContrabandDropInterval <= TimeSpan.Zero)
            Rotate();
        else if (!DropLeaked && _timing.CurTime >= _dropLeaks)
            Leak();
    }

    /// <summary>
    /// Moves the drop to another open station that buys contraband, never the same one twice running when there is a
    /// choice, and tells the Underground. Returns the new drop, or null if the rotation is off or no station can take it.
    /// </summary>
    public EntityUid? Rotate()
    {
        var profile = Profile;
        var candidates = profile.ContrabandDropInterval > TimeSpan.Zero ? DropCandidates(profile) : new List<EntityUid>();
        if (candidates.Count > 1 && DropPoint is { } previous)
            candidates.Remove(previous);

        if (candidates.Count == 0)
        {
            DropPoint = null;
            return null;
        }

        var drop = _random.Pick(candidates);
        var now = _timing.CurTime;
        DropPoint = drop;
        DropEnds = now + profile.ContrabandDropInterval;
        _dropLeaks = now + profile.ContrabandDropLeak;
        DropLeaked = false;

        _market.TellCompanies(profile.UndergroundCompanies, Loc.GetString("wg-trade-contraband-drop",
            ("station", Name(drop)), ("minutes", (int) profile.ContrabandDropInterval.TotalMinutes)));
        return drop;
    }

    /// <summary>Tips security off on where the drop is, once per drop.</summary>
    public void Leak()
    {
        if (DropLeaked || !DropActive)
            return;

        DropLeaked = true;
        _market.Radio(Loc.GetString("wg-trade-contraband-drop-leak", ("station", Name(DropPoint!.Value))), Profile.ContrabandAlertChannel);
    }

    /// <summary>Why a chute refuses a crate because it is contraband and the chute isn't at the drop, or null if it takes it.</summary>
    public LocId? CheckDrop(EntityUid chute, EntityUid crate)
    {
        if (MetaData(crate).EntityPrototype?.ID is not { } good
            || !AtDropOnly(good)
            || Transform(chute).GridUid == DropPoint
            || _market.IsHiddenChute(chute))
            return null;

        return "wg-trade-refuse-drop";
    }

    /// <summary>Whether a good is contraband that sells at the drop alone right now.</summary>
    public bool AtDropOnly(string good)
    {
        return DropActive && Profile.ContrabandGoods.Contains(good);
    }

    /// <summary>The open stations the drop may move to: those with a chute listing contraband.</summary>
    private List<EntityUid> DropCandidates(WGTradeMarketPrototype profile)
    {
        return _sector.OpenChutes()
            .Where(chute => profile.ContrabandGoods.Any(good => chute.Dispenser.Inventory.ContainsKey(good.Id)))
            .Select(chute => chute.Grid)
            .Distinct()
            .ToList();
    }
}
