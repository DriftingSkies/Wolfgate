using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server._WF.Feedback;
using Content.Shared._WF.Feedback;
using Content.Shared.Symphony;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Symphony;

/// <summary>
/// How the SSymphony panel answers a player's bug report or suggestion. The panel reads the reports straight from
/// wolfgate_feedback_report; to move one on it posts the new status here, which writes the row and tells the player
/// in game, at once if they are online and at their next login if not. A panel that wrote the row itself, with the
/// server down say, posts to the refresh path instead so anyone online is told. Behind the admin API token.
/// </summary>
public sealed partial class SymphonyFeedbackSystem : EntitySystem
{
    /// <summary>Status host path that takes a POST to set a report's status.</summary>
    public const string StatusPath = "/symphony/feedback/status";

    /// <summary>Status host path that takes a POST to show players the statuses the panel wrote itself.</summary>
    public const string RefreshPath = "/symphony/feedback/refresh";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private FeedbackSystem _feedback = default!;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread; the work itself hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != StatusPath && path != RefreshPath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;
        if (context.RequestMethod != HttpMethod.Post)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        if (path == StatusPath)
            await RespondStatus(context);
        else
            await RespondRefresh(context);
        return true;
    }

    private async Task RespondStatus(IStatusHandlerContext context)
    {
        StatusBody? body = null;
        try
        {
            body = await context.RequestBodyJsonAsync<StatusBody>();
        }
        catch (Exception)
        {
            // Not JSON: refused below.
        }

        if (body?.Id is not { } id || !FeedbackRules.TryParseStatus(body.Status, out var status))
        {
            await context.RespondAsync(
                $"expected {{\"id\": 1, \"status\": \"{string.Join('|', FeedbackRules.Statuses)}\", \"message\": \"optional\", \"notify\": true}}",
                HttpStatusCode.BadRequest);
            return;
        }

        var notify = body.Notify ?? true;
        // Started on the main thread, so the database's answer comes back to it and the notice goes out from there.
        var online = await await _tasks.OnMainThread(() => _feedback.SetStatusAsync(id, status, body.Message, notify));
        if (online == null)
        {
            await context.RespondErrorAsync(HttpStatusCode.NotFound);
            return;
        }

        Log.Info($"{body.Actor ?? "The panel"} set report #{id} to {FeedbackRules.StatusId(status)}");
        await context.RespondJsonAsync(new StatusResponse
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Id = id,
            Status = FeedbackRules.StatusId(status),
            Notify = notify,
            PlayerOnline = online.Value,
        });
    }

    private async Task RespondRefresh(IStatusHandlerContext context)
    {
        // No body, or no users in it, means everyone online. Named users must all be real ids: a refresh meant for
        // a few players never falls back to everyone.
        using var reader = new StreamReader(context.RequestBody);
        var text = await reader.ReadToEndAsync();
        RefreshBody? body = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                body = JsonSerializer.Deserialize<RefreshBody>(text);
            }
            catch (JsonException)
            {
                await context.RespondAsync("expected {\"users\": [\"<uuid>\", ...]} or no body", HttpStatusCode.BadRequest);
                return;
            }
        }

        List<Guid>? wanted = null;
        if (body?.Users is { } users)
        {
            wanted = new List<Guid>();
            foreach (var raw in users)
            {
                if (!Guid.TryParse(raw, out var user))
                {
                    await context.RespondAsync($"not a user id: {raw}", HttpStatusCode.BadRequest);
                    return;
                }

                wanted.Add(user);
            }

            if (wanted.Count == 0)
            {
                await context.RespondAsync("users is empty; leave it out to refresh everyone", HttpStatusCode.BadRequest);
                return;
            }
        }

        var told = await _tasks.OnMainThread(() =>
        {
            var targets = wanted ?? _players.Sessions.Select(s => s.UserId.UserId).ToList();
            return targets.Count(_feedback.NotifyPlayer);
        });

        await context.RespondJsonAsync(new RefreshResponse { SymphonyModule = SharedSymphony.ModuleVersion, Online = told });
    }

    private sealed class StatusBody
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("notify")] public bool? Notify { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class StatusResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("notify")] public bool Notify { get; set; }
        [JsonPropertyName("player_online")] public bool PlayerOnline { get; set; }
    }

    private sealed class RefreshBody
    {
        [JsonPropertyName("users")] public List<string>? Users { get; set; }
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("online")] public int Online { get; set; }
    }
}
