using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Content.Server._WF.Census;
using Content.Shared._WF.Census;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.Symphony;

/// <summary>
/// How the SSymphony panel sets up censuses: it lists them with their answer counts, creates and updates them
/// (opening one tells the players online) and deletes them. The panel reads the answers straight from
/// wolfgate_census_response. Behind the admin API token; works with wf.census.enabled off too.
/// </summary>
public sealed class SymphonyCensusSystem : EntitySystem
{
    /// <summary>GET lists the censuses, POST creates or updates one.</summary>
    public const string CensusPath = "/symphony/census";

    /// <summary>POST deletes a census and its answers.</summary>
    public const string DeletePath = "/symphony/census/delete";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private CensusSystem _census = default!;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread; the work itself hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != CensusPath && path != DeletePath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;

        try
        {
            if (path == CensusPath && context.RequestMethod == HttpMethod.Get)
                await RespondList(context);
            else if (context.RequestMethod != HttpMethod.Post)
                await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            else if (path == CensusPath)
                await RespondSave(context);
            else
                await RespondDelete(context);
        }
        catch (Exception e)
        {
            Log.Error($"Census endpoint {path} failed: {e}");
            await context.RespondErrorAsync(HttpStatusCode.InternalServerError);
        }

        return true;
    }

    private async Task RespondList(IStatusHandlerContext context)
    {
        var rows = await await _tasks.OnMainThread(() => _census.ListAsync());
        var now = DateTime.UtcNow;
        var list = new JsonArray();
        foreach (var row in rows)
        {
            list.Add(ToJson(row, now));
        }

        await context.RespondJsonAsync(new JsonObject
        {
            ["symphony_module"] = SharedSymphony.ModuleVersion,
            ["censuses"] = list,
        });
    }

    private async Task RespondSave(IStatusHandlerContext context)
    {
        using var body = await ReadBody(context);
        if (body?.RootElement is not { ValueKind: JsonValueKind.Object } root)
        {
            await Refuse(context, HttpStatusCode.BadRequest, "bad_request", "The body must be a JSON object.");
            return;
        }

        foreach (var field in new[] { "title", "questions", "state" })
        {
            if (!root.TryGetProperty(field, out _))
            {
                await Refuse(context, HttpStatusCode.BadRequest, "bad_request", $"The body has no {field}.");
                return;
            }
        }

        if (ReadSave(root, out var problem) is not { } save)
        {
            await Refuse(context, HttpStatusCode.BadRequest, "invalid", problem!);
            return;
        }

        var (outcome, row) = await await _tasks.OnMainThread(() => _census.SaveAsync(save));
        switch (outcome)
        {
            case CensusSaveOutcome.NotFound:
                await Refuse(context, HttpStatusCode.NotFound, "not_found", $"There is no census #{save.Id}.");
                return;
            case CensusSaveOutcome.HasResponses:
                await Refuse(context, HttpStatusCode.Conflict, "has_responses",
                    $"Census #{save.Id} has {row!.Responses} responses, so its questions and anonymous can no longer change; only title, description, closes_at and state can.");
                return;
        }

        Log.Info($"{save.Actor ?? "The panel"} {(save.Id == null ? "created" : "updated")} census #{row!.Census.Id} ({row.Census.State}): {row.Census.Title}");
        await context.RespondJsonAsync(ToJson(row, DateTime.UtcNow));
    }

    private async Task RespondDelete(IStatusHandlerContext context)
    {
        using var body = await ReadBody(context);
        if (body?.RootElement is not { ValueKind: JsonValueKind.Object } root
            || !root.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.Number
            || !idProp.TryGetInt32(out var id))
        {
            await Refuse(context, HttpStatusCode.BadRequest, "bad_request", "Expected {\"id\": 1, \"actor\": \"name\"}.");
            return;
        }

        var actor = root.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (!await await _tasks.OnMainThread(() => _census.DeleteAsync(id)))
        {
            await Refuse(context, HttpStatusCode.NotFound, "not_found", $"There is no census #{id}.");
            return;
        }

        Log.Info($"{actor ?? "The panel"} deleted census #{id}");
        await context.RespondJsonAsync(new JsonObject
        {
            ["symphony_module"] = SharedSymphony.ModuleVersion,
            ["id"] = id,
            ["deleted"] = true,
        });
    }

    /// <summary>The census in the body with every field checked, or a sentence naming the field at fault.</summary>
    private static CensusSave? ReadSave(JsonElement root, out string? problem)
    {
        problem = null;
        var save = new CensusSave();

        if (root.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null)
        {
            if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var value) || value <= 0)
            {
                problem = "id must be null or a census number.";
                return null;
            }

            save.Id = value;
        }

        var title = root.GetProperty("title");
        save.Title = title.ValueKind == JsonValueKind.String ? title.GetString()!.Trim() : "";
        if (save.Title.Length is 0 or > CensusRules.MaxTitle)
        {
            problem = $"title must be 1 to {CensusRules.MaxTitle} characters.";
            return null;
        }

        if (root.TryGetProperty("description", out var description) && description.ValueKind != JsonValueKind.Null)
        {
            if (description.ValueKind != JsonValueKind.String || description.GetString()!.Trim().Length > CensusRules.MaxDescription)
            {
                problem = $"description must be text of at most {CensusRules.MaxDescription} characters.";
                return null;
            }

            save.Description = description.GetString()!.Trim();
        }

        if (CensusQuestions.Parse(root.GetProperty("questions"), out problem) is not { } questions)
            return null;

        save.Questions = questions;

        if (root.TryGetProperty("anonymous", out var anonymous) && anonymous.ValueKind != JsonValueKind.Null)
        {
            if (anonymous.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                problem = "anonymous must be true or false.";
                return null;
            }

            save.Anonymous = anonymous.GetBoolean();
        }

        var state = root.GetProperty("state");
        save.State = state.ValueKind == JsonValueKind.String ? state.GetString()! : "";
        if (save.State is not (CensusRules.StateDraft or CensusRules.StateOpen or CensusRules.StateClosed))
        {
            problem = "state must be draft, open or closed.";
            return null;
        }

        if (root.TryGetProperty("closes_at", out var closes) && closes.ValueKind != JsonValueKind.Null)
        {
            if (closes.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(closes.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
            {
                problem = "closes_at must be an ISO 8601 time or null.";
                return null;
            }

            save.ClosesAt = when.UtcDateTime;
        }

        if (save.State == CensusRules.StateOpen && save.ClosesAt <= DateTime.UtcNow)
        {
            problem = "closes_at is in the past, so the census cannot be open; move it later or clear it.";
            return null;
        }

        save.Actor = root.TryGetProperty("actor", out var actor) && actor.ValueKind == JsonValueKind.String ? actor.GetString() : null;
        return save;
    }

    private static JsonObject ToJson(CensusRow row, DateTime now)
    {
        var census = row.Census;
        JsonNode? questions;
        try
        {
            questions = CensusQuestions.ParseStored(census.Questions) is { } parsed
                ? CensusQuestions.ToJson(parsed)
                : JsonNode.Parse(census.Questions);
        }
        catch (JsonException)
        {
            questions = new JsonArray();
        }

        var state = CensusSystem.EffectiveState(census, now);
        var closedAt = census.ClosedAt ?? (state == CensusRules.StateClosed ? census.ClosesAt : null);
        return new JsonObject
        {
            ["id"] = census.Id,
            ["title"] = census.Title,
            ["description"] = census.Description,
            ["questions"] = questions,
            ["state"] = state,
            ["anonymous"] = census.Anonymous,
            ["created_by"] = census.CreatedBy,
            ["created_at"] = Iso(census.CreatedAt),
            ["opened_at"] = Iso(census.OpenedAt),
            ["closes_at"] = Iso(census.ClosesAt),
            ["closed_at"] = Iso(closedAt),
            ["responses"] = row.Responses,
        };
    }

    private static string? Iso(DateTime? time)
    {
        return time is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture) : null;
    }

    private static async Task<JsonDocument?> ReadBody(IStatusHandlerContext context)
    {
        try
        {
            return await JsonDocument.ParseAsync(context.RequestBody);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task Refuse(IStatusHandlerContext context, HttpStatusCode code, string error, string message)
    {
        return context.RespondJsonAsync(new JsonObject { ["error"] = error, ["message"] = message }, code);
    }
}
