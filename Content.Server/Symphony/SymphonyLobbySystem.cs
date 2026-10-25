using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Content.Server.Audio;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Content.Shared.Symphony;
using Robust.Server.Player;
using Robust.Server.ServerStatus;
using Robust.Server.Upload;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Symphony;

/// <summary>
/// The lobby's title screen art and music, set from the SSymphony panel. The panel can pick any background or track
/// the build ships, or send its own: an image or an Ogg Vorbis track posted here is shared with every client through
/// the uploaded-resources channel and shown or played at once. Whatever is set holds over every round, and over a
/// restart, until it is cleared. The game keeps only the files in use; the panel keeps the library. Behind the admin
/// API token.
/// </summary>
public sealed partial class SymphonyLobbySystem : EntitySystem
{
    /// <summary>GET: what the lobby shows and plays, and what it could.</summary>
    public const string StatePath = "/symphony/lobby";

    /// <summary>GET ?path=: one background or track as bytes, for the panel's previews.</summary>
    public const string FilePath = "/symphony/lobby/file";

    /// <summary>POST ?kind=background|music: a file as the raw body, shared with every client. Nothing is shown yet.</summary>
    public const string UploadPath = "/symphony/lobby/upload";

    /// <summary>POST {"path": "..."} shows that background now; {"path": null} goes back to a random one.</summary>
    public const string BackgroundPath = "/symphony/lobby/background";

    /// <summary>POST {"tracks": [...]} plays that playlist now; {"tracks": null} goes back to the shuffled collection.</summary>
    public const string MusicPath = "/symphony/lobby/music";

    /// <summary>POST {"title", "body", "style"} shows a notice in the lobby; an empty title and body take it away.</summary>
    public const string NoticePath = "/symphony/lobby/notice";

    /// <summary>POST {"message", "color"}: a server announcement in everyone's chat, as the update notice is sent.</summary>
    public const string AnnouncePath = "/symphony/announce";

    private const int MaxNoticeTitle = 120;
    private const int MaxNoticeBody = 1500;
    private const int MaxAnnouncement = 1000;
    private static readonly string[] NoticeStyles = ["info", "warning", "event"];

    public const int MaxImageBytes = 8_000_000;
    public const int MaxTrackBytes = 16_000_000;
    private const int MaxTracks = 30;

    private static readonly ResPath UploadDir = new("Symphony/Lobby");
    private static readonly ResPath SavedDir = new("/symphony/lobby");
    private static readonly ResPath StateFile = new("/symphony/lobby/state.json");

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private NetworkResourceManager _netRes = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private ContentAudioSystem _audio = default!;
    [Dependency] private IChatManager _chat = default!;

    /// <summary>The lobby notice now, or an empty one.</summary>
    private SymphonyLobbyNoticeEvent _notice = new(null, null, "info");

    /// <summary>Files this server shares, by their /Uploaded path, with their size. Only those in use are kept.</summary>
    private readonly Dictionary<string, int> _uploads = new();

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnJoinedLobby);
        Restore();
    }

    private void OnJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        if (!_notice.IsEmpty)
            RaiseNetworkEvent(_notice, ev.PlayerSession);
    }

    // Runs on the status host's thread; the work itself hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        var path = context.Url.AbsolutePath;
        if (path != StatePath && path != FilePath && path != UploadPath && path != BackgroundPath && path != MusicPath && path != NoticePath && path != AnnouncePath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;

        var get = context.RequestMethod == HttpMethod.Get;
        if ((path == StatePath || path == FilePath) != get || (!get && context.RequestMethod != HttpMethod.Post))
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        switch (path)
        {
            case StatePath:
                await context.RespondJsonAsync(await _tasks.OnMainThread(BuildState));
                break;
            case FilePath:
                await RespondFile(context);
                break;
            case UploadPath:
                await RespondUpload(context);
                break;
            case BackgroundPath:
                await RespondBackground(context);
                break;
            case NoticePath:
                await RespondNotice(context);
                break;
            case AnnouncePath:
                await RespondAnnounce(context);
                break;
            default:
                await RespondMusic(context);
                break;
        }
        return true;
    }

    private LobbyState BuildState()
    {
        var lobby = LobbySessions().Count;
        return new LobbyState
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            UploadsSupported = SymphonyUploads.Supported,
            MaxImageBytes = MaxImageBytes,
            MaxTrackBytes = MaxTrackBytes,
            Players = _players.PlayerCount,
            InLobby = lobby,
            Background = new BackgroundState
            {
                Current = _ticker.LobbyBackground,
                Override = _ticker.LobbyBackgroundOverride,
                Choices = _ticker.LobbyBackgroundChoices.Select(p => p.ToString()).ToList(),
            },
            Music = new MusicState
            {
                Playlist = _audio.LobbyPlaylist.ToList(),
                Override = _audio.LobbyPlaylistOverride?.ToList(),
                Collection = _audio.LobbyCollection.ToList(),
            },
            Uploads = _uploads.Select(kv => new UploadState { Path = kv.Key, Size = kv.Value }).ToList(),
            Notice = _notice.IsEmpty ? null : new NoticeBody { Title = _notice.Title, Body = _notice.Body, Style = _notice.Style },
        };
    }

    private async Task RespondNotice(IStatusHandlerContext context)
    {
        NoticeBody? body;
        try
        {
            body = await context.RequestBodyJsonAsync<NoticeBody>();
        }
        catch (Exception)
        {
            body = null;
        }
        var title = body?.Title?.Trim() ?? "";
        var text = body?.Body?.Trim() ?? "";
        var style = body?.Style ?? "info";
        if (body == null || title.Length > MaxNoticeTitle || text.Length > MaxNoticeBody || !NoticeStyles.Contains(style))
        {
            await context.RespondAsync($"expected {{\"title\": \"...\", \"body\": \"...\", \"style\": \"info|warning|event\"}}, a title of at most {MaxNoticeTitle} and a body of at most {MaxNoticeBody} characters; empty takes the notice away", HttpStatusCode.BadRequest);
            return;
        }
        var outcome = await _tasks.OnMainThread(() =>
        {
            _notice = new SymphonyLobbyNoticeEvent(title == "" ? null : title, text == "" ? null : text, style);
            // Everyone has it now, so those in a round see it the moment they are back in the lobby.
            RaiseNetworkEvent(_notice);
            WriteState();
            return new ApplyResponse { SymphonyModule = SharedSymphony.ModuleVersion, Custom = !_notice.IsEmpty, Players = _players.PlayerCount, InLobby = LobbySessions().Count };
        });
        Log.Info($"{body.Actor ?? "The panel"} {(title == "" && text == "" ? "took the lobby notice away" : $"set the lobby notice: {title}")}");
        await context.RespondJsonAsync(outcome);
    }

    private async Task RespondAnnounce(IStatusHandlerContext context)
    {
        AnnounceBody? body;
        try
        {
            body = await context.RequestBodyJsonAsync<AnnounceBody>();
        }
        catch (Exception)
        {
            body = null;
        }
        var message = body?.Message?.Trim() ?? "";
        Color? color = null;
        if (body?.Color is { Length: > 0 } hex && Color.TryFromHex(hex, out var parsed))
            color = parsed;
        if (message == "" || message.Length > MaxAnnouncement || (body?.Color is { Length: > 0 } && color == null))
        {
            await context.RespondAsync($"expected {{\"message\": \"...\", \"color\": \"#rrggbb\"}}, a message of 1 to {MaxAnnouncement} characters", HttpStatusCode.BadRequest);
            return;
        }
        var players = await _tasks.OnMainThread(() =>
        {
            _chat.DispatchServerAnnouncement(message, color);
            return _players.PlayerCount;
        });
        Log.Info($"{body!.Actor ?? "The panel"} announced: {message}");
        await context.RespondJsonAsync(new ApplyResponse { SymphonyModule = SharedSymphony.ModuleVersion, Players = players });
    }

    /// <summary>Players not in a round: the ones a change is shown or played to at once.</summary>
    private List<ICommonSession> LobbySessions()
    {
        return _players.Sessions
            .Where(s => !_ticker.PlayerGameStatuses.TryGetValue(s.UserId, out var status) || status != PlayerGameStatus.JoinedGame)
            .ToList();
    }

    private async Task RespondFile(IStatusHandlerContext context)
    {
        var wanted = HttpUtility.ParseQueryString(context.Url.Query)["path"] ?? "";
        var found = await _tasks.OnMainThread(() =>
        {
            // Only what the lobby could show: never an arbitrary resource by path.
            var known = _ticker.LobbyBackgroundChoices.Any(p => p.ToString() == wanted)
                        || _audio.LobbyCollection.Contains(wanted)
                        || _uploads.ContainsKey(wanted);
            if (!known)
                return null;
            try
            {
                using var stream = _resources.ContentFileRead(new ResPath(wanted));
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
            catch (Exception)
            {
                return null;
            }
        });
        if (found == null)
        {
            await context.RespondErrorAsync(HttpStatusCode.NotFound);
            return;
        }
        await context.RespondAsync(found, HttpStatusCode.OK, ContentTypeOf(wanted));
    }

    private async Task RespondUpload(IStatusHandlerContext context)
    {
        var kind = HttpUtility.ParseQueryString(context.Url.Query)["kind"];
        if (kind != "background" && kind != "music")
        {
            await context.RespondAsync("kind is background or music", HttpStatusCode.BadRequest);
            return;
        }
        if (!SymphonyUploads.Supported)
        {
            await context.RespondAsync("this engine build cannot share a file with clients; pick one the build ships", HttpStatusCode.NotImplemented);
            return;
        }

        var limit = kind == "background" ? MaxImageBytes : MaxTrackBytes;
        byte[] data;
        using (var copy = new MemoryStream())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await context.RequestBody.ReadAsync(buffer)) > 0)
            {
                copy.Write(buffer, 0, read);
                if (copy.Length > limit)
                {
                    await context.RespondAsync($"the file is over {limit / 1_000_000} MB", HttpStatusCode.RequestEntityTooLarge);
                    return;
                }
            }
            data = copy.ToArray();
        }

        var ext = kind == "background" ? ImageExtension(data) : TrackExtension(data);
        if (ext == null)
        {
            await context.RespondAsync(kind == "background"
                ? "not a PNG, JPEG or WebP image"
                : "not an Ogg Vorbis or WAV track; the game plays nothing else", HttpStatusCode.UnsupportedMediaType);
            return;
        }

        // Named by what is in it, so the same file sent twice is one file, and a client never shows a stale one.
        var hash = Convert.ToHexString(SHA256.HashData(data))[..16].ToLowerInvariant();
        var relative = UploadDir / (kind == "background" ? "art" : "music") / $"{hash}.{ext}";
        var path = (SymphonyUploads.Root / relative).ToString();

        var sent = await _tasks.OnMainThread(() =>
        {
            if (_uploads.ContainsKey(path))
                return -1;
            var n = SymphonyUploads.Put(_netRes, _net, relative, data);
            _uploads[path] = data.Length;
            Save(relative, data);
            return n;
        });

        await context.RespondJsonAsync(new UploadResponse
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Path = path,
            Size = data.Length,
            Fresh = sent >= 0,
            SentTo = Math.Max(0, sent),
        });
    }

    private async Task RespondBackground(IStatusHandlerContext context)
    {
        BackgroundBody? body;
        try
        {
            body = await context.RequestBodyJsonAsync<BackgroundBody>();
        }
        catch (Exception)
        {
            body = null;
        }
        if (body == null)
        {
            await context.RespondAsync("expected {\"path\": \"/Textures/...\"} or {\"path\": null}", HttpStatusCode.BadRequest);
            return;
        }

        var outcome = await _tasks.OnMainThread(() =>
        {
            if (body.Path != null && !_ticker.LobbyBackgroundChoices.Any(p => p.ToString() == body.Path) && !IsUpload(body.Path, "art"))
                return null;
            _ticker.SetLobbyBackgroundOverride(body.Path, announce: true);
            Prune("art", body.Path == null ? [] : [body.Path]);
            WriteState();
            return new ApplyResponse
            {
                SymphonyModule = SharedSymphony.ModuleVersion,
                Current = _ticker.LobbyBackground,
                Override = _ticker.LobbyBackgroundOverride,
                Custom = _ticker.LobbyBackgroundOverride != null,
                Players = _players.PlayerCount,
                InLobby = LobbySessions().Count,
            };
        });
        if (outcome == null)
        {
            await context.RespondAsync("not a background this server has: pick one the build ships, or upload it first", HttpStatusCode.NotFound);
            return;
        }
        Log.Info($"{body.Actor ?? "The panel"} set the lobby background to {body.Path ?? "random"}");
        await context.RespondJsonAsync(outcome);
    }

    private async Task RespondMusic(IStatusHandlerContext context)
    {
        MusicBody? body;
        try
        {
            body = await context.RequestBodyJsonAsync<MusicBody>();
        }
        catch (Exception)
        {
            body = null;
        }
        if (body == null || body.Tracks is { Count: 0 } || body.Tracks?.Count > MaxTracks)
        {
            await context.RespondAsync($"expected {{\"tracks\": [\"/Audio/...\", ...]}} with 1 to {MaxTracks} tracks, or {{\"tracks\": null}}", HttpStatusCode.BadRequest);
            return;
        }

        var outcome = await _tasks.OnMainThread(() =>
        {
            var collection = _audio.LobbyCollection;
            var unknown = body.Tracks?.FirstOrDefault(t => !collection.Contains(t) && !IsUpload(t, "music"));
            if (unknown != null)
                return (ApplyResponse?) null;
            var lobby = LobbySessions();
            _audio.SetLobbyPlaylistOverride(body.Tracks?.ToArray(), lobby);
            Prune("music", body.Tracks ?? []);
            WriteState();
            return new ApplyResponse
            {
                SymphonyModule = SharedSymphony.ModuleVersion,
                Playlist = _audio.LobbyPlaylist.ToList(),
                Custom = _audio.LobbyPlaylistOverride != null,
                Players = _players.PlayerCount,
                InLobby = lobby.Count,
            };
        });
        if (outcome == null)
        {
            await context.RespondAsync("a track is not one this server has: pick from the collection, or upload it first", HttpStatusCode.NotFound);
            return;
        }
        Log.Info($"{body.Actor ?? "The panel"} set the lobby playlist to {(body.Tracks == null ? "the shuffled collection" : string.Join(", ", body.Tracks))}");
        await context.RespondJsonAsync(outcome);
    }

    private bool IsUpload(string path, string kind)
    {
        return _uploads.ContainsKey(path) && path.StartsWith((SymphonyUploads.Root / UploadDir / kind).ToString() + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Drop the uploads of one kind nothing uses any more, so joining players are not sent them. A kind of its own,
    /// so a track sent ahead of its playlist is not dropped by a background change in between.
    /// </summary>
    private void Prune(string kind, IReadOnlyCollection<string> keep)
    {
        var prefix = (SymphonyUploads.Root / UploadDir / kind).ToString() + "/";
        foreach (var path in _uploads.Keys.Where(p => p.StartsWith(prefix, StringComparison.Ordinal) && !keep.Contains(p)).ToList())
        {
            _uploads.Remove(path);
            var relative = new ResPath(path).RelativeTo(SymphonyUploads.Root);
            SymphonyUploads.Remove(_netRes, relative);
            try
            {
                var saved = SavedDir / relative.Filename;
                if (_resources.UserData.Exists(saved))
                    _resources.UserData.Delete(saved);
            }
            catch (Exception e)
            {
                Log.Warning($"Could not delete the saved copy of {path}: {e.Message}");
            }
        }
    }

    private void Save(ResPath relative, byte[] data)
    {
        try
        {
            _resources.UserData.CreateDir(SavedDir);
            using var file = _resources.UserData.OpenWrite(SavedDir / relative.Filename);
            file.Write(data);
        }
        catch (Exception e)
        {
            Log.Error($"Could not save {relative}, so it lasts only until the next restart: {e.Message}");
        }
    }

    private void WriteState()
    {
        try
        {
            _resources.UserData.CreateDir(SavedDir);
            _resources.UserData.WriteAllText(StateFile, JsonSerializer.Serialize(new SavedState
            {
                Background = _ticker.LobbyBackgroundOverride,
                Playlist = _audio.LobbyPlaylistOverride?.ToList(),
                Notice = _notice.IsEmpty ? null : new NoticeBody { Title = _notice.Title, Body = _notice.Body, Style = _notice.Style },
            }));
        }
        catch (Exception e)
        {
            Log.Error($"Could not write {StateFile}, so the lobby goes back to random at the next restart: {e.Message}");
        }
    }

    /// <summary>What was set before a restart: the uploads it uses loaded back, then set again.</summary>
    private void Restore()
    {
        SavedState? state;
        try
        {
            if (!_resources.UserData.Exists(StateFile))
                return;
            state = JsonSerializer.Deserialize<SavedState>(_resources.UserData.ReadAllText(StateFile));
        }
        catch (Exception e)
        {
            Log.Error($"Could not read {StateFile}, so the lobby is random: {e.Message}");
            return;
        }
        if (state == null)
            return;
        if (state.Notice is { } saved && NoticeStyles.Contains(saved.Style ?? "info"))
            _notice = new SymphonyLobbyNoticeEvent(saved.Title, saved.Body, saved.Style ?? "info");

        bool Available(string path)
        {
            // A file the build ships must still be in it: a content update may have taken it away since it was set.
            if (!path.StartsWith(SymphonyUploads.Root + "/", StringComparison.Ordinal))
                return _resources.ContentFileExists(new ResPath(path));
            if (_uploads.ContainsKey(path))
                return true;
            var relative = new ResPath(path).RelativeTo(SymphonyUploads.Root);
            try
            {
                var saved = SavedDir / relative.Filename;
                if (!SymphonyUploads.Supported || !_resources.UserData.Exists(saved))
                    return false;
                using var stream = _resources.UserData.OpenRead(saved);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                SymphonyUploads.Load(_netRes, relative, copy.ToArray());
                _uploads[path] = (int) copy.Length;
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Could not load {path} back: {e.Message}");
                return false;
            }
        }

        if (state.Background != null && Available(state.Background))
            _ticker.SetLobbyBackgroundOverride(state.Background, announce: false);
        var tracks = state.Playlist?.Where(Available).ToArray();
        if (tracks is { Length: > 0 })
            _audio.SetLobbyPlaylistOverride(tracks, null);
    }

    /// <summary>The extension a picture's own bytes say it is, or null when they say it is no picture the game shows.</summary>
    public static string? ImageExtension(ReadOnlySpan<byte> d)
    {
        if (d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
            return "png";
        if (d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
            return "jpg";
        if (d.Length > 12 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8))
            return "webp";
        return null;
    }

    /// <summary>The extension a track's own bytes say it is: Ogg Vorbis or WAV, the two the client plays.</summary>
    public static string? TrackExtension(ReadOnlySpan<byte> d)
    {
        // An Ogg file's first packet names its codec; Opus in Ogg looks the same from outside and does not play.
        // The packet starts after the 27-byte page header and its table of segment lengths.
        if (d.Length > 64 && d[..4].SequenceEqual("OggS"u8))
        {
            var p = 27 + d[26];
            return d.Length > p + 7 && d[p] == 0x01 && d[(p + 1)..(p + 7)].SequenceEqual("vorbis"u8) ? "ogg" : null;
        }
        if (d.Length > 12 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WAVE"u8))
            return "wav";
        return null;
    }

    private static string ContentTypeOf(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            _ => "application/octet-stream",
        };
    }

    private sealed class LobbyState
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("uploads_supported")] public bool UploadsSupported { get; set; }
        [JsonPropertyName("max_image_bytes")] public int MaxImageBytes { get; set; }
        [JsonPropertyName("max_track_bytes")] public int MaxTrackBytes { get; set; }
        [JsonPropertyName("players")] public int Players { get; set; }
        [JsonPropertyName("in_lobby")] public int InLobby { get; set; }
        [JsonPropertyName("background")] public BackgroundState Background { get; set; } = new();
        [JsonPropertyName("music")] public MusicState Music { get; set; } = new();
        [JsonPropertyName("uploads")] public List<UploadState> Uploads { get; set; } = [];
        [JsonPropertyName("notice")] public NoticeBody? Notice { get; set; }
    }

    private sealed class NoticeBody
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("style")] public string? Style { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class AnnounceBody
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("color")] public string? Color { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class BackgroundState
    {
        [JsonPropertyName("current")] public string? Current { get; set; }
        [JsonPropertyName("override")] public string? Override { get; set; }
        [JsonPropertyName("choices")] public List<string> Choices { get; set; } = [];
    }

    private sealed class MusicState
    {
        [JsonPropertyName("playlist")] public List<string> Playlist { get; set; } = [];
        [JsonPropertyName("override")] public List<string>? Override { get; set; }
        [JsonPropertyName("collection")] public List<string> Collection { get; set; } = [];
    }

    private sealed class UploadState
    {
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("size")] public int Size { get; set; }
    }

    private sealed class UploadResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("size")] public int Size { get; set; }
        [JsonPropertyName("fresh")] public bool Fresh { get; set; }
        [JsonPropertyName("sent_to")] public int SentTo { get; set; }
    }

    private sealed class ApplyResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("current")] public string? Current { get; set; }
        [JsonPropertyName("override")] public string? Override { get; set; }
        [JsonPropertyName("playlist")] public List<string>? Playlist { get; set; }
        [JsonPropertyName("custom")] public bool Custom { get; set; }
        [JsonPropertyName("players")] public int Players { get; set; }
        [JsonPropertyName("in_lobby")] public int InLobby { get; set; }
    }

    private sealed class BackgroundBody
    {
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class MusicBody
    {
        [JsonPropertyName("tracks")] public List<string>? Tracks { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class SavedState
    {
        [JsonPropertyName("background")] public string? Background { get; set; }
        [JsonPropertyName("playlist")] public List<string>? Playlist { get; set; }
        [JsonPropertyName("notice")] public NoticeBody? Notice { get; set; }
    }
}
