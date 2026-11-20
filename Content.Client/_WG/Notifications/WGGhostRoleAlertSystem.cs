using Content.Client.Ghost;
using Content.Shared._WG.Notifications;
using Content.Shared.Ghost;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WG.Notifications;

/// <summary>Plays a sound and flashes the window when the number of open ghost roles goes up.</summary>
public sealed class WGGhostRoleAlertSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private AudioSystem _audio = default!;

    private const string Sound = "/Audio/_CP14/Effects/crystal_ding.ogg";

    private int? _lastCount;

    public override void Initialize()
    {
        base.Initialize();
        _ghost.GhostRoleCountUpdated += OnCountUpdated;
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(_ => _lastCount = null);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _ghost.GhostRoleCountUpdated -= OnCountUpdated;
    }

    /// <summary>
    /// Whether a count update means a new role opened. The first count after becoming a ghost is the current state,
    /// not a new role.
    /// </summary>
    public static bool IsNewRole(int? last, int now) => last is { } previous && now > previous;

    private void OnCountUpdated(GhostUpdateGhostRoleCountEvent ev)
    {
        var rose = IsNewRole(_lastCount, ev.AvailableGhostRoles);
        _lastCount = ev.AvailableGhostRoles;
        if (!rose)
            return;

        if (_cfg.GetCVar(NotificationCVars.SoundGhostRole))
            _audio.PlayGlobal(Sound, Filter.Local(), false, AudioParams.Default.WithVolume(-4f));

        WGWindowFlash.Request(NotificationCVars.FlashGhostRole);
    }
}
