using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._WG.Announcements;
using Content.Shared.Chat;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.Announcements;

/// <summary>Sends admin roleplay announcements: chat text, a chosen sound and a notification chip.</summary>
public sealed partial class WGAnnounceSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private TransformSystem _transform = default!;

    private static readonly ResPath AudioRoot = new("/Audio");

    /// <summary>Checks and sends an announcement. Returns a loc id for the error, or null once it is out.</summary>
    public string? TrySend(ICommonSession admin, WGAnnounceRequest request)
    {
        if (Validate(request) is { } error)
            return error;

        Filter filter;
        switch (request.Target)
        {
            case WGAnnounceTarget.AdminMap:
                if (admin.AttachedEntity is not { } attached)
                    return "wg-announce-error-no-map";
                filter = Filter.BroadcastMap(_transform.GetMapId(attached));
                break;
            default:
                filter = Filter.Broadcast();
                break;
        }

        return Send(request, filter, $"{admin:player}");
    }

    /// <summary>Checks and sends an announcement to a filter, for systems with no admin behind them.</summary>
    public string? Send(WGAnnounceRequest request, Filter filter, string by)
    {
        if (Validate(request) is { } error)
            return error;

        if (request.PostToChat)
        {
            _chat.ChatMessageToManyFiltered(filter,
                ChatChannel.Radio,
                WGAnnounceFormat.PlainText(request),
                WGAnnounceFormat.ChatMarkup(request),
                EntityUid.Invalid,
                false,
                true,
                WGAnnounceFormat.BodyColor(request.Accent));
        }

        var sound = request.SoundMode switch
        {
            WGAnnounceSoundMode.Default => WGAnnounceFormat.DefaultSound,
            WGAnnounceSoundMode.Custom => request.SoundPath,
            _ => null,
        };
        if (sound != null)
            _audio.PlayGlobal(new SoundPathSpecifier(sound), filter, true, AudioParams.Default.WithVolume(request.Volume));

        if (request.ShowChip)
        {
            RaiseNetworkEvent(new WGAnnouncementChipEvent
            {
                Level = request.ChipLevel,
                Accent = request.Accent,
                Title = request.ChipTitle.Trim(),
                Text = request.ChipText.Trim(),
                Sender = WGAnnounceFormat.SenderOrDefault(request.Sender),
                Duration = request.ChipDuration,
                Flash = request.ChipFlash,
            }, filter);
        }

        _adminLog.Add(LogType.Chat, LogImpact.Medium,
            $"{by} sent a roleplay announcement to {request.Target} as {request.Sender}: {WGAnnounceFormat.PlainText(request)} (chip: {(request.ShowChip ? request.ChipText : "none")}, sound: {sound ?? "none"})");
        return null;
    }

    private string? Validate(WGAnnounceRequest request)
    {
        if (request.Sender.Length > WGAnnounceLimits.MaxSender
            || request.Title.Length > WGAnnounceLimits.MaxTitle
            || request.Body.Length > WGAnnounceLimits.MaxBody
            || request.ChipTitle.Length > WGAnnounceLimits.MaxChipTitle
            || request.ChipText.Length > WGAnnounceLimits.MaxChipText)
            return "wg-announce-error-too-long";

        if (!request.PostToChat && !request.ShowChip && request.SoundMode == WGAnnounceSoundMode.None)
            return "wg-announce-error-nothing";

        if (request.PostToChat && string.IsNullOrWhiteSpace(request.Body))
            return "wg-announce-error-empty-body";

        if (request.ShowChip && string.IsNullOrWhiteSpace(request.ChipText))
            return "wg-announce-error-empty-chip";

        if (!float.IsFinite(request.Volume) || !float.IsFinite(request.ChipDuration))
            return "wg-announce-error-bad-number";

        request.Volume = Math.Clamp(request.Volume, WGAnnounceLimits.MinVolume, WGAnnounceLimits.MaxVolume);
        request.ChipDuration = Math.Clamp(request.ChipDuration, 0f, WGAnnounceLimits.MaxChipDuration);
        request.Accent = request.Accent.WithAlpha(1f);

        if (request.SoundMode == WGAnnounceSoundMode.Custom && !IsValidSound(request.SoundPath))
            return "wg-announce-error-bad-sound";

        return null;
    }

    /// <summary>
    /// Whether a path is a sound file under /Audio. Packaged servers drop the audio files and keep only their
    /// <see cref="AudioMetadataPrototype"/>, so either counts.
    /// </summary>
    public bool IsValidSound(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var resPath = new ResPath(path.Trim()).Clean();
        return resPath.IsRooted
            && resPath.TryRelativeTo(AudioRoot, out _)
            && resPath.Extension is "ogg" or "wav"
            && (_resources.ContentFileExists(resPath) || _prototypes.HasIndex<AudioMetadataPrototype>(resPath.ToString()));
    }
}
