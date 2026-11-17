using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;

namespace Content.Server.Symphony;

/// <summary>
/// What this build carries on top of its branch. A test merge build made through the publish workflow ships
/// Resources/Symphony/testmerges.json, written by Tools/_WG/Ci/test_merge.py: the pull requests merged before packaging,
/// and any that were asked for but did not merge. Read once at startup; a build without the file has none.
/// </summary>
public static class SymphonyTestMerges
{
    /// <summary>The stamp test_merge.py writes into Resources.</summary>
    public const string StampPath = "/Symphony/testmerges.json";

    /// <summary>A pull request merged into this build.</summary>
    public sealed record Merged(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("author")] string Author,
        [property: JsonPropertyName("sha")] string Sha,
        [property: JsonPropertyName("url")] string Url);

    /// <summary>A pull request asked for but not merged, with why.</summary>
    public sealed record Failed(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed class Stamp
    {
        [JsonPropertyName("base")] public string? Base { get; set; }
        [JsonPropertyName("built_at")] public string? BuiltAt { get; set; }
        [JsonPropertyName("merged")] public List<Merged>? Merged { get; set; }
        [JsonPropertyName("failed")] public List<Failed>? Failed { get; set; }
    }

    private static readonly object Gate = new();
    private static bool _loaded;
    private static IReadOnlyList<Merged> _merged = Array.Empty<Merged>();
    private static IReadOnlyList<Failed> _failed = Array.Empty<Failed>();
    private static string _base = "";

    /// <summary>Pull requests merged into this build.</summary>
    public static IReadOnlyList<Merged> MergedPullRequests
    {
        get { lock (Gate) return _merged; }
    }

    /// <summary>Pull requests that were asked for but did not merge.</summary>
    public static IReadOnlyList<Failed> FailedPullRequests
    {
        get { lock (Gate) return _failed; }
    }

    /// <summary>
    /// The list as the status host reports it under symphony_test_merges. Built fresh per request: a JsonNode
    /// cannot sit in two documents, and the status handler runs off the main thread, so it only reads what
    /// <see cref="Load"/> left here.
    /// </summary>
    public static JsonNode StatusJson()
    {
        IReadOnlyList<Merged> mergedList;
        IReadOnlyList<Failed> failedList;
        string baseSha;
        lock (Gate)
        {
            mergedList = _merged;
            failedList = _failed;
            baseSha = _base;
        }

        var merged = new JsonArray();
        foreach (var pr in mergedList)
        {
            merged.Add(new JsonObject
            {
                ["number"] = pr.Number, ["title"] = pr.Title, ["author"] = pr.Author, ["sha"] = pr.Sha, ["url"] = pr.Url,
            });
        }

        var failed = new JsonArray();
        foreach (var pr in failedList)
            failed.Add(new JsonObject { ["number"] = pr.Number, ["title"] = pr.Title, ["reason"] = pr.Reason });

        return new JsonObject { ["base"] = baseSha, ["merged"] = merged, ["failed"] = failed };
    }

    /// <summary>
    /// Reads the stamp once, from the main thread at startup: IoC and the resource manager are not for the status
    /// host's thread, which only ever reads the result.
    /// </summary>
    public static void Load(IResourceManager resources, ISawmill sawmill)
    {
        lock (Gate)
        {
            if (_loaded)
                return;
            _loaded = true;
            if (!resources.ContentFileExists(StampPath))
                return;

            try
            {
                var stamp = JsonSerializer.Deserialize<Stamp>(resources.ContentFileReadAllText(StampPath));
                _merged = stamp?.Merged?.Where(pr => pr.Number > 0).ToList() ?? new List<Merged>();
                _failed = stamp?.Failed?.Where(pr => pr.Number > 0).ToList() ?? new List<Failed>();
                _base = stamp?.Base ?? "";
                sawmill.Info($"Test merge build: {_merged.Count} merged, {_failed.Count} skipped");
            }
            catch (Exception e)
            {
                sawmill.Error($"Could not read {StampPath}: {e.Message}");
            }
        }
    }
}

/// <summary>
/// Tells players what is test merged, the way TGS does: once at round start to everyone, and to each player as
/// they join the lobby. Silent on a build with nothing merged.
/// </summary>
public sealed partial class SymphonyTestMergeSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IResourceManager _resources = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Here, on the main thread, and not lazily from the status host: that thread has no IoC to resolve with.
        SymphonyTestMerges.Load(_resources, Log);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnPlayerJoinedLobby);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent ev)
    {
        if (ev.New != GameRunLevel.InRound || SymphonyTestMerges.MergedPullRequests.Count == 0)
            return;
        _chat.DispatchServerAnnouncement(Message());
    }

    private void OnPlayerJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (SymphonyTestMerges.MergedPullRequests.Count == 0)
            return;
        _chat.DispatchServerMessage(ev.PlayerSession, Message());
    }

    private string Message()
    {
        var merged = SymphonyTestMerges.MergedPullRequests;
        var list = string.Join(", ", merged.Select(pr => Loc.GetString("symphony-test-merge-entry",
            ("number", pr.Number), ("title", pr.Title), ("author", pr.Author))));
        return Loc.GetString("symphony-test-merges-active", ("count", merged.Count), ("list", list));
    }
}
