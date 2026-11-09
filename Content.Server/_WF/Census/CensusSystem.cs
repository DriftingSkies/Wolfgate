using System.Linq;
using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._WF.Census;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.Census;

/// <summary>
/// Puts the censuses staff open from the SSymphony panel to the players and stores their answers, one per player
/// and census. Players get the open censuses and whether they answered each, nothing more. The window opens for
/// a player in the lobby once per census and connection; one in a round gets a chat line instead.
/// </summary>
public sealed class CensusSystem : EntitySystem
{
    private static readonly TimeSpan CloseCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ListInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;

    private List<OpenCensus> _open = new();
    private bool _loaded;
    private bool _reloading;
    private TimeSpan _nextCloseCheck;

    // Loaded at login; a player with no entry yet gets nothing.
    private readonly Dictionary<NetUserId, HashSet<int>> _answered = new();
    // The censuses whose window a player has been shown this connection.
    private readonly Dictionary<NetUserId, HashSet<int>> _prompted = new();
    private readonly Dictionary<NetUserId, Queue<TimeSpan>> _submits = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextList = new();
    private readonly HashSet<NetUserId> _sending = new();

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        SubscribeNetworkEvent<CensusSubmitEvent>(OnSubmit);
        SubscribeNetworkEvent<CensusListRequestEvent>(OnListRequest);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnJoinedLobby);
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_reloading || _timing.RealTime < _nextCloseCheck)
            return;

        // Also retries a first read that failed.
        _nextCloseCheck = _timing.RealTime + CloseCheckInterval;
        var now = DateTime.UtcNow;
        if (!_loaded || _open.Any(c => c.ClosesAt <= now))
            _ = ReloadAsync();
    }

    /// <summary>The state a census is in, counting one whose closes_at has passed as closed.</summary>
    public static string EffectiveState(WolfgateCensus census, DateTime now)
    {
        return census.State == CensusRules.StateOpen && census.ClosesAt <= now ? CensusRules.StateClosed : census.State;
    }

    /// <summary>Every census with its answer count, newest first, closing any whose time has come.</summary>
    public async Task<List<CensusRow>> ListAsync()
    {
        await _db.CloseElapsedCensusesAsync(DateTime.UtcNow);
        return await _db.GetCensusesAsync(false);
    }

    /// <summary>
    /// Creates or updates a census from the panel and tells online players when it has just opened. The caller has
    /// checked the fields; this checks what needs the row.
    /// </summary>
    public async Task<(CensusSaveOutcome Outcome, CensusRow? Row)> SaveAsync(CensusSave save)
    {
        var now = DateTime.UtcNow;
        await _db.CloseElapsedCensusesAsync(now);

        var questions = CensusQuestions.Write(save.Questions);
        WolfgateCensus census;
        var responses = 0;
        string? before = null;
        if (save.Id is { } id)
        {
            if (await _db.GetCensusAsync(id) is not { } row)
                return (CensusSaveOutcome.NotFound, null);

            census = row.Census;
            responses = row.Responses;
            before = census.State;
            // Compared normalised, so the same questions sent back with defaults filled in still match.
            var stored = CensusQuestions.ParseStored(census.Questions) is { } parsed ? CensusQuestions.Write(parsed) : census.Questions;
            if (responses > 0 && (census.Anonymous != save.Anonymous || stored != questions))
                return (CensusSaveOutcome.HasResponses, row);
        }
        else
        {
            census = new WolfgateCensus { CreatedBy = save.Actor ?? "", CreatedAt = now };
        }

        census.Title = save.Title;
        census.Description = save.Description;
        census.Questions = questions;
        census.Anonymous = save.Anonymous;
        census.ClosesAt = save.ClosesAt;
        switch (save.State)
        {
            case CensusRules.StateOpen:
                census.OpenedAt ??= now;
                census.ClosedAt = null;
                break;
            case CensusRules.StateClosed:
                if (before != CensusRules.StateClosed || census.ClosedAt == null)
                    census.ClosedAt = now;
                break;
            default:
                census.ClosedAt = null;
                break;
        }

        census.State = save.State;
        census.Responses = null!;
        await _db.SaveCensusAsync(census);

        var opened = save.State == CensusRules.StateOpen && before != CensusRules.StateOpen;
        await ReloadAsync(opened ? census.Id : null);
        return (CensusSaveOutcome.Saved, new CensusRow(census, responses));
    }

    /// <summary>Deletes a census and its answers. False when there was none.</summary>
    public async Task<bool> DeleteAsync(int id)
    {
        if (!await _db.DeleteCensusAsync(id))
            return false;

        foreach (var answered in _answered.Values)
        {
            answered.Remove(id);
        }

        await ReloadAsync();
        return true;
    }

    /// <summary>Reads the open censuses again and sends them to everyone online, announcing one that just opened.</summary>
    private async Task ReloadAsync(int? announce = null)
    {
        _reloading = true;
        try
        {
            await _db.CloseElapsedCensusesAsync(DateTime.UtcNow);
            var rows = await _db.GetCensusesAsync(true);
            var open = new List<OpenCensus>();
            foreach (var row in rows)
            {
                if (CensusQuestions.ParseStored(row.Census.Questions) is not { } questions)
                {
                    Log.Error($"Census #{row.Census.Id} has questions that break the rules; players are not shown it.");
                    continue;
                }

                open.Add(new OpenCensus(row.Census.Id, row.Census.Title, row.Census.Description, row.Census.ClosesAt, row.Census.Anonymous, questions));
            }

            _open = open;
            _loaded = true;
            foreach (var session in _players.Sessions)
            {
                Send(session, announce);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Could not read the open censuses: {e}");
        }
        finally
        {
            _reloading = false;
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        var user = args.Session.UserId;
        if (args.NewStatus == SessionStatus.InGame)
        {
            LoadPlayer(args.Session);
        }
        else if (args.NewStatus == SessionStatus.Disconnected)
        {
            _answered.Remove(user);
            _prompted.Remove(user);
            _submits.Remove(user);
            _nextList.Remove(user);
        }
    }

    private async void LoadPlayer(ICommonSession session)
    {
        try
        {
            var answered = await _db.GetCensusesAnsweredAsync(session.UserId);
            if (session.Status != SessionStatus.InGame)
                return;

            _answered[session.UserId] = answered;
            Send(session);
        }
        catch (Exception e)
        {
            Log.Error($"Could not read the censuses {session.Name} answered: {e}");
        }
    }

    private void OnJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        Send(ev.PlayerSession);
    }

    private void OnListRequest(CensusListRequestEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        var now = _timing.RealTime;
        if (_nextList.GetValueOrDefault(user) > now)
            return;

        _nextList[user] = now + ListInterval;
        Send(args.SenderSession);
    }

    private bool InLobby(ICommonSession session)
    {
        return session.AttachedEntity == null && !_ticker.UserHasJoinedGame(session);
    }

    /// <summary>Sends a player the open censuses, opening the window or writing a chat line as the rules say.</summary>
    private void Send(ICommonSession session, int? announce = null)
    {
        if (session.Status != SessionStatus.InGame || !_answered.TryGetValue(session.UserId, out var answered))
            return;

        var enabled = _cfg.GetCVar(CensusCVars.Enabled);
        var lobby = InLobby(session);
        var prompt = false;
        if (enabled && lobby)
        {
            var prompted = _prompted.GetOrNew(session.UserId);
            foreach (var census in _open)
            {
                if (!answered.Contains(census.Id) && prompted.Add(census.Id))
                    prompt = true;
            }
        }

        RaiseNetworkEvent(new CensusListEvent
        {
            Censuses = _open.Select(c => c.ToEntry(answered.Contains(c.Id))).ToList(),
            Prompt = prompt,
        }, session);

        if (enabled && !lobby && announce is { } id && !answered.Contains(id) && _open.FirstOrDefault(c => c.Id == id) is { } opened)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("wf-census-opened-chat",
                ("title", FormattedMessage.EscapeText(opened.Title))));
        }
    }

    private async void OnSubmit(CensusSubmitEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var user = session.UserId;

        if (!_cfg.GetCVar(CensusCVars.Enabled))
        {
            Answer(session, ev.CensusId, "wf-census-error-disabled");
            return;
        }

        var now = _timing.RealTime;
        var sent = _submits.GetOrNew(user);
        while (sent.Count > 0 && sent.Peek() + CensusRules.SubmitWindow <= now)
        {
            sent.Dequeue();
        }

        if (sent.Count >= CensusRules.SubmitsPerWindow || !_sending.Add(user))
        {
            var wait = sent.Count > 0 ? sent.Peek() + CensusRules.SubmitWindow - now : TimeSpan.FromSeconds(1);
            Answer(session, ev.CensusId, "wf-census-error-rate", count: Math.Max(1, (int) Math.Ceiling(wait.TotalSeconds)));
            return;
        }

        sent.Enqueue(now);
        try
        {
            var census = _open.FirstOrDefault(c => c.Id == ev.CensusId);
            if (census == null || census.ClosesAt <= DateTime.UtcNow)
            {
                Answer(session, ev.CensusId, "wf-census-error-not-open");
                return;
            }

            if (!_answered.TryGetValue(user, out var answered))
            {
                Answer(session, ev.CensusId, "wf-census-error-failed");
                return;
            }

            if (answered.Contains(census.Id))
            {
                Answer(session, ev.CensusId, "wf-census-error-answered");
                return;
            }

            if (CensusQuestions.Answer(census.Questions, ev, out var error, out var question, out var count) is not { } json)
            {
                Answer(session, ev.CensusId, error ?? "wf-census-error-invalid", question, count);
                return;
            }

            var stored = await _db.AddCensusResponseAsync(new WolfgateCensusResponse
            {
                CensusId = census.Id,
                PlayerUserId = user.UserId,
                Answers = json,
                CreatedAt = DateTime.UtcNow,
            });

            // Answered either way: a refusal means an answer is already stored.
            if (_answered.TryGetValue(user, out var current))
                current.Add(census.Id);

            if (!stored)
            {
                Answer(session, census.Id, "wf-census-error-answered");
                return;
            }

            Log.Info(census.Anonymous
                ? $"A player answered census #{census.Id}"
                : $"{session.Name} answered census #{census.Id}");
            Answer(session, census.Id, null);
            Send(session);
        }
        catch (Exception e)
        {
            Log.Error($"Could not store the census answers of {session.Name}: {e}");
            Answer(session, ev.CensusId, "wf-census-error-failed");
        }
        finally
        {
            _sending.Remove(user);
        }
    }

    private void Answer(ICommonSession session, int id, string? error, string question = "", int count = 0)
    {
        if (session.Status == SessionStatus.Disconnected)
            return;

        RaiseNetworkEvent(new CensusResultEvent { CensusId = id, Error = error, Question = question, Count = count }, session);
    }

    private sealed record OpenCensus(int Id, string Title, string Description, DateTime? ClosesAt, bool Anonymous, List<CensusQuestion> Questions)
    {
        public CensusEntry ToEntry(bool answered)
        {
            return new CensusEntry
            {
                Id = Id,
                Title = Title,
                Description = Description,
                ClosesAt = ClosesAt,
                Answered = answered,
                Questions = answered ? new List<CensusQuestion>() : Questions,
            };
        }
    }
}

/// <summary>A census as the panel sends it, already checked field by field.</summary>
public sealed class CensusSave
{
    public int? Id;
    public string Title = "";
    public string Description = "";
    public List<CensusQuestion> Questions = new();
    public bool Anonymous;
    public DateTime? ClosesAt;
    public string State = CensusRules.StateDraft;
    public string? Actor;
}

public enum CensusSaveOutcome
{
    Saved,
    NotFound,
    HasResponses,
}
