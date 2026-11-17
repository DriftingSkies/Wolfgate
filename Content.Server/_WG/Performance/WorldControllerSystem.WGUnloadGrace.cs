using Content.Server.Worldgen.Components;
using Content.Shared._WG.Performance;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;

namespace Content.Server.Worldgen.Systems;

public sealed partial class WorldControllerSystem
{
    [Dependency] private IConfigurationManager _wgCfg = default!;

    /// <summary>When each loaded chunk stopped being needed, for chunks waiting out the unload grace.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _wgUnneededSince = new();
    private TimeSpan _wgUnloadGrace;

    private void WGInitializeUnloadGrace()
    {
        _wgCfg.OnValueChanged(WGPerformanceCVars.ChunkUnloadGrace, s => _wgUnloadGrace = TimeSpan.FromSeconds(s), true);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _wgUnneededSince.Clear());
    }

    /// <summary>
    /// Whether a loaded chunk nothing needs should unload now. It stays loaded until the grace has passed since it was
    /// last needed; a chunk that is needed again starts over.
    /// </summary>
    private bool WGShouldUnload(EntityUid chunk, bool needed)
    {
        if (needed)
        {
            _wgUnneededSince.Remove(chunk);
            return false;
        }

        var now = _gameTiming.CurTime;
        if (!_wgUnneededSince.TryGetValue(chunk, out var since))
        {
            _wgUnneededSince[chunk] = since = now;
        }

        if (now - since < _wgUnloadGrace)
            return false;

        _wgUnneededSince.Remove(chunk);
        return true;
    }
}
