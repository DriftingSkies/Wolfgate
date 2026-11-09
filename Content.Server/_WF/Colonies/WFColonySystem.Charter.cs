using System.Linq;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Database;
using Content.Shared._WF.Colonies;

namespace Content.Server._WF.Colonies;

/// <summary>
/// The charter: from Charter tier the company with the largest share governs its world. It passes to a rival that leads by
/// a quarter for three daily checks, and lapses when the governor's share or the world's tier falls. Its powers are control,
/// never income: permits for newcomers, first pick of the outpost lots, the orbital batteries and, at Dominion, the yard.
/// </summary>
public sealed partial class WFColonySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    /// <summary>Prefix of a holder with an open yard berth in a charter's stored list.</summary>
    private const string BerthPrefix = "b:";

    private sealed class CharterState
    {
        public int CompanyId;
        public DateTime Granted;
        public int? Rival;
        public int Streak;
        public DateTime LastCheck;
        public bool Defence;
        public bool Open;

        /// <summary>Holders the governor let found here, and holders with an open yard berth under <see cref="BerthPrefix"/>.</summary>
        public HashSet<string> Permits = new();

        public bool Dirty;
    }

    private void InitializeCharter()
    {
        SubscribeLocalEvent<WFOutpostFoundAttemptEvent>(OnFoundAttempt);
        SubscribeLocalEvent<WFOutpostLotsEvent>(OnLots);
        SubscribeLocalEvent<WFOutpostComponent, WFOrbitalEngageEvent>(OnOrbitalEngage);
    }

    /// <summary>A governed world's batteries engage campaign ships in its orbit when its governor says so.</summary>
    private void OnOrbitalEngage(Entity<WFOutpostComponent> outpost, ref WFOrbitalEngageEvent args)
    {
        if (TryWorldOf(outpost.Comp.Ground, out var world, out var network) && Tier(world) >= WFWorldTier.Charter
            && DefenceEngaged(world))
            args.Orbit = Comp<Planets.WFPlanetNetworkComponent>(network).OrbitMap;
    }

    /// <summary>The company governing a world, or null.</summary>
    public int? Governor(string world) => _worlds.TryGetValue(world, out var state) ? state.Charter?.CompanyId : null;

    /// <summary>Whether a world's governor has its orbital batteries engage campaign ships.</summary>
    public bool DefenceEngaged(string world) => _worlds.TryGetValue(world, out var state) && state.Charter is { Defence: true };

    /// <summary>Whether a company may lay down builds on a world's yard: its governor, or a company it opened a berth to.</summary>
    public bool HasBerth(string world, int company)
    {
        return _worlds.TryGetValue(world, out var state) && state.Charter is { } charter
            && (charter.CompanyId == company || charter.Permits.Contains(BerthPrefix + CompanyKey(company)));
    }

    private static WFStoredCharter StoredCharter(string world, CharterState charter)
    {
        return new WFStoredCharter(world, charter.CompanyId, charter.Granted, charter.Rival, charter.Streak, charter.LastCheck,
            charter.Defence, charter.Open, string.Join(',', charter.Permits));
    }

    /// <summary>Runs the daily charter check on every world that is due one.</summary>
    private void CheckCharters()
    {
        var now = DateTime.UtcNow;
        foreach (var world in _worlds.Values)
        {
            var daily = world.Charter is { } charter && now - charter.LastCheck >= CheckInterval;
            CheckCharter(world, now, daily);
        }
    }

    /// <summary>Checks a world's charter as of <paramref name="now"/>; a daily check also counts toward succession.</summary>
    public void CheckCharter(string world, DateTime now, bool daily)
    {
        if (_worlds.TryGetValue(world, out var state))
            CheckCharter(state, now, daily);
    }

    private void CheckCharter(WorldState world, DateTime now, bool daily)
    {
        if (world.Charter is not { } charter)
        {
            if (world.Tier >= WFWorldTier.Charter && Candidate(world, out var company, _cfg.GetCVar(ColonyCVars.CharterShare)))
                Grant(world, company, now);
            return;
        }

        if (world.Tier < WFWorldTier.Charter || _companies.CompanyIdentity(charter.CompanyId) == null
            || Share(world.Id, CompanyKey(charter.CompanyId)) < _cfg.GetCVar(ColonyCVars.CharterLapseShare))
        {
            EndCharter(world);
            CheckCharter(world, now, false);
            return;
        }

        if (!daily)
            return;

        charter.LastCheck = now;
        charter.Dirty = true;
        var governing = Points(world.Id, CompanyKey(charter.CompanyId));
        var lead = 1 + _cfg.GetCVar(ColonyCVars.SuccessionLead);
        if (!Candidate(world, out var rival, 0f, except: charter.CompanyId)
            || Points(world.Id, CompanyKey(rival)) < governing * lead)
        {
            charter.Rival = null;
            charter.Streak = 0;
            return;
        }

        charter.Streak = charter.Rival == rival ? charter.Streak + 1 : 1;
        charter.Rival = rival;
        if (charter.Streak >= _cfg.GetCVar(ColonyCVars.SuccessionChecks))
            Grant(world, rival, now);
    }

    /// <summary>The eligible company with the most points on a world: enough members and at least <paramref name="share"/>.</summary>
    private bool Candidate(WorldState world, out int company, float share, int? except = null)
    {
        company = 0;
        var best = 0.0;
        var members = _cfg.GetCVar(ColonyCVars.CharterMembers);
        foreach (var holder in world.Holders.Values)
        {
            if (CompanyOfKey(holder.Key) is not { } id || id == except || holder.Points <= best
                || Share(world.Id, holder.Key) < share || _companies.MemberPlayers(id) < members)
                continue;

            best = holder.Points;
            company = id;
        }

        return best > 0;
    }

    private void Grant(WorldState world, int company, DateTime now)
    {
        var old = world.Charter;
        world.Charter = new CharterState
        {
            CompanyId = company,
            Granted = now,
            LastCheck = now,
            Defence = old?.Defence ?? false,
            Dirty = true,
        };

        var name = HolderName(world, CompanyKey(company));
        _log.Info($"Company {company} ({name}) took the charter of {world.Id}.");
        _market.Radio(Loc.GetString(old == null ? "wf-colony-charter-granted" : "wf-colony-charter-passed",
            ("world", WorldName(world.Id)), ("company", name)));
        Track(_db.SetWorldCharterAsync(world.Id, StoredCharter(world.Id, world.Charter)));
        world.Charter.Dirty = false;
        RefreshTerritory(world);
        CharterChanged(world.Id);
    }

    private void EndCharter(WorldState world)
    {
        if (world.Charter is not { } charter)
            return;

        _log.Info($"The charter of {world.Id} held by company {charter.CompanyId} lapsed.");
        _market.Radio(Loc.GetString("wf-colony-charter-lapsed", ("world", WorldName(world.Id)),
            ("company", HolderName(world, CompanyKey(charter.CompanyId)))));
        world.Charter = null;
        Track(_db.SetWorldCharterAsync(world.Id, null));
        RefreshTerritory(world);
        CharterChanged(world.Id);
    }

    /// <summary>Lets other systems, such as the yard, follow a world's charter changing hands.</summary>
    private void CharterChanged(string world)
    {
        var ev = new WFColonyCharterChangedEvent(world, Governor(world));
        RaiseLocalEvent(ref ev);
    }

    #region Permits and lots

    /// <summary>The holders an account founds for: its character's company, and itself.</summary>
    private List<string> FounderHolders(Robust.Shared.Network.NetUserId owner)
    {
        var holders = new List<string> { UserKey(owner) };
        if (_players.TryGetSessionById(owner, out var session) && _companies.CompanyOfSession(session) is { } company)
            holders.Add(CompanyKey(company.Id));
        return holders;
    }

    /// <summary>A newcomer to a governed world needs the governor's permit to found there; a holder already there never does.</summary>
    private void OnFoundAttempt(ref WFOutpostFoundAttemptEvent args)
    {
        if (args.Refusal != null || !TryWorldOf(args.Ground, out var id, out _) || !_worlds.TryGetValue(id, out var world)
            || world.Charter is not { } charter || charter.Open)
            return;

        var holders = FounderHolders(args.Owner);
        if (holders.Any(holder => Points(id, holder) > 0 || charter.Permits.Contains(holder))
            || holders.Contains(CompanyKey(charter.CompanyId)))
            return;

        var name = holders[^1];
        if (_players.TryGetSessionById(args.Owner, out var session))
            HolderOf(session, out name);
        world.Applicants[holders[^1]] = name;
        args.Refusal = Loc.GetString("wf-colony-permit-needed", ("world", WorldName(id)),
            ("company", HolderName(world, CompanyKey(charter.CompanyId))));
    }

    /// <summary>For the first minutes of a round a governed world's outpost lots wait for its governor's outposts.</summary>
    private void OnLots(ref WFOutpostLotsEvent args)
    {
        if (_ticker.RoundDuration() >= TimeSpan.FromMinutes(_cfg.GetCVar(ColonyCVars.LotHold))
            || !TryWorldOf(args.Ground, out var id, out _) || !_worlds.TryGetValue(id, out var world)
            || world.Charter is not { } charter)
            return;

        if (!FounderHolders(args.Owner).Contains(CompanyKey(charter.CompanyId)))
            args.SkipLots = true;
    }

    #endregion

    #region Governor's controls

    /// <summary>Whether a player belongs to a world's governing company.</summary>
    public bool IsGovernor(string world, Robust.Shared.Player.ICommonSession session)
    {
        return Governor(world) is { } company && _companies.CompanyOfSession(session)?.Id == company;
    }

    private bool TryCharterFor(string world, Robust.Shared.Player.ICommonSession session, out CharterState charter)
    {
        charter = default!;
        if (!_worlds.TryGetValue(world, out var state) || state.Charter is not { } held || !IsGovernor(world, session))
            return false;

        charter = held;
        return true;
    }

    /// <summary>Sets whether a world's batteries engage campaign ships in orbit.</summary>
    public bool SetDefence(string world, Robust.Shared.Player.ICommonSession session, bool engage)
    {
        if (!TryCharterFor(world, session, out var charter))
            return false;

        charter.Defence = engage;
        charter.Dirty = true;
        return true;
    }

    /// <summary>Opens a world to every founder, or closes it to newcomers without a permit.</summary>
    public bool SetOpen(string world, Robust.Shared.Player.ICommonSession session, bool open)
    {
        if (!TryCharterFor(world, session, out var charter))
            return false;

        charter.Open = open;
        charter.Dirty = true;
        return true;
    }

    /// <summary>Grants or withdraws a holder's permit to found on a world.</summary>
    public bool SetPermit(string world, Robust.Shared.Player.ICommonSession session, string holder, bool granted)
    {
        if (!TryCharterFor(world, session, out var charter) || holder.StartsWith(BerthPrefix))
            return false;

        if (granted)
            charter.Permits.Add(holder);
        else
            charter.Permits.Remove(holder);
        _worlds[world].Applicants.Remove(holder);
        charter.Dirty = true;
        return true;
    }

    /// <summary>Opens or closes a yard berth to a company holding enough of the world.</summary>
    public bool SetBerth(string world, Robust.Shared.Player.ICommonSession session, string holder, bool open)
    {
        if (!TryCharterFor(world, session, out var charter) || CompanyOfKey(holder) is not { } company
            || company == charter.CompanyId
            || open && Share(world, holder) < _cfg.GetCVar(ColonyCVars.BerthShare))
            return false;

        if (open)
            charter.Permits.Add(BerthPrefix + holder);
        else
            charter.Permits.Remove(BerthPrefix + holder);
        charter.Dirty = true;
        return true;
    }

    #endregion
}

/// <summary>Raised broadcast when a world's charter changes hands or lapses.</summary>
[ByRefEvent]
public readonly record struct WFColonyCharterChangedEvent(string World, int? Governor);
