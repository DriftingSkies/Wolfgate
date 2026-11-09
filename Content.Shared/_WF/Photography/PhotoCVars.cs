using Robust.Shared.Configuration;

namespace Content.Shared._WF.Photography;

/// <summary>Server settings for photos.</summary>
[CVarDefs]
public sealed class PhotoCVars
{
    /// <summary>Days a stored photo nothing holds is kept before the startup cleanup deletes it.</summary>
    public static readonly CVarDef<int> KeepUnusedDays =
        CVarDef.Create("wf.photo.keep_unused_days", 90, CVar.SERVERONLY);

    /// <summary>Seconds the server waits for a client's capture before giving up on it.</summary>
    public static readonly CVarDef<float> CaptureTimeout =
        CVarDef.Create("wf.photo.capture_timeout", 10f, CVar.SERVERONLY);

    /// <summary>Photo images one client may download per minute.</summary>
    public static readonly CVarDef<int> DownloadsPerMinute =
        CVarDef.Create("wf.photo.downloads_per_minute", 120, CVar.SERVERONLY);

    /// <summary>Saved albums a character gets back when it spawns. 0 turns personal albums off.</summary>
    public static readonly CVarDef<int> AlbumsPerCharacter =
        CVarDef.Create("wf.photo.albums_per_character", 1, CVar.SERVERONLY);

    /// <summary>Minutes between saves of albums that changed. Albums also save at round end.</summary>
    public static readonly CVarDef<float> AlbumSaveMinutes =
        CVarDef.Create("wf.photo.album_save_minutes", 5f, CVar.SERVERONLY);
}
