using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Symphony;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Symphony;

/// <summary>
/// Who is connected and what they are doing, for the SSymphony panel's player list. The admin API's /admin/info
/// carries only a name and whether they are an admin, so the panel could show a job of "admin" and nothing else.
/// This answers with the character, the job, whether they are in the round or in the lobby, and their ping: the
/// same things the in-game admin menu reads, from the same systems. Behind the admin API token, like the rest.
/// </summary>
public sealed partial class SymphonyPlayersSystem : EntitySystem
{
    /// <summary>Status host path that answers GET with the player list.</summary>
    public const string PlayersPath = "/symphony/players";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private SharedMindSystem _minds = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedRoleSystem _roles = default!;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread; everything it reads is main-thread state, so it all hops across.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        if (context.Url.AbsolutePath != PlayersPath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;
        if (context.RequestMethod != HttpMethod.Get)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        await context.RespondJsonAsync(await _tasks.OnMainThread(Snapshot));
        return true;
    }

    private PlayersResponse Snapshot()
    {
        var ticker = EntityManager.System<GameTicker>();
        var players = new List<PlayerEntry>();

        foreach (var session in _players.Sessions)
        {
            var admin = _admins.GetAdminData(session, includeDeAdmin: true);
            var character = "";
            if (session.AttachedEntity is { } entity && TryComp<MetaDataComponent>(entity, out var meta))
                character = meta.EntityName;

            var job = "";
            var antag = false;
            var antags = new List<string>();
            if (_minds.TryGetMind(session, out var mindId, out _))
            {
                job = _jobs.MindTryGetJobName(mindId);
                antag = _roles.MindIsAntagonist(mindId);
                if (antag)
                    antags.AddRange(_roles.MindGetAllRoleInfo(mindId).Where(r => r.Antagonist).Select(r => r.Name));
            }

            players.Add(new PlayerEntry
            {
                UserId = session.UserId.UserId.ToString(),
                Name = session.Name,
                Character = character,
                Job = job,
                State = StateOf(ticker, session),
                Ping = session.Channel?.Ping ?? 0,
                IsAdmin = admin != null,
                Deadminned = admin is { Active: false },
                Stealth = admin is { Stealth: true },
                AdminRank = admin?.Title ?? "",
                IsAntag = antag,
                Antags = antags,
            });
        }

        return new PlayersResponse { SymphonyModule = SharedSymphony.ModuleVersion, RoundId = ticker.RoundId, Players = players };
    }

    /// <summary>
    /// Where this player is, in the words the panel shows: still connecting, in the lobby and ready or not,
    /// observing the round, or playing it.
    /// </summary>
    private string StateOf(GameTicker ticker, Robust.Shared.Player.ICommonSession session)
    {
        if (session.Status != SessionStatus.InGame)
            return "connecting";
        if (!ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) || status != PlayerGameStatus.JoinedGame)
            return status == PlayerGameStatus.ReadyToPlay ? "ready" : "lobby";
        if (session.AttachedEntity is { } entity && HasComp<GhostComponent>(entity))
            return "ghost";
        return session.AttachedEntity == null ? "joining" : "playing";
    }

    private sealed class PlayersResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("round_id")] public int RoundId { get; set; }
        [JsonPropertyName("players")] public List<PlayerEntry> Players { get; set; } = new();
    }

    private sealed class PlayerEntry
    {
        [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("character")] public string Character { get; set; } = "";
        [JsonPropertyName("job")] public string Job { get; set; } = "";
        [JsonPropertyName("state")] public string State { get; set; } = "";
        [JsonPropertyName("ping")] public int Ping { get; set; }
        [JsonPropertyName("is_admin")] public bool IsAdmin { get; set; }
        [JsonPropertyName("deadminned")] public bool Deadminned { get; set; }
        // Hidden from adminwho: the panel must not count or name them anywhere players can see.
        [JsonPropertyName("stealth")] public bool Stealth { get; set; }
        [JsonPropertyName("admin_rank")] public string AdminRank { get; set; } = "";
        [JsonPropertyName("is_antag")] public bool IsAntag { get; set; }
        [JsonPropertyName("antags")] public List<string> Antags { get; set; } = new();
    }
}
