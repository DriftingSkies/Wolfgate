using Content.Server.Worldgen.Components;
using Content.Shared._WF.Performance;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;

namespace Content.Server.Worldgen.Systems;

public sealed partial class WorldControllerSystem
{
    [Dependency] private IConfigurationManager _wfCfg = default!;

    /// <summary>When each loaded chunk stopped being needed, for chunks waiting out the unload grace.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _wfUnneededSince = new();
    private TimeSpan _wfUnloadGrace;

    private void WFInitializeUnloadGrace()
    {
        _wfCfg.OnValueChanged(WFPerformanceCVars.ChunkUnloadGrace, s => _wfUnloadGrace = TimeSpan.FromSeconds(s), true);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _wfUnneededSince.Clear());
    }

    /// <summary>
    /// Whether a loaded chunk nothing needs should unload now. It stays loaded until the grace has passed since it was
    /// last needed; a chunk that is needed again starts over.
    /// </summary>
    private bool WFShouldUnload(EntityUid chunk, bool needed)
    {
        if (needed)
        {
            _wfUnneededSince.Remove(chunk);
            return false;
        }

        var now = _gameTiming.CurTime;
        if (!_wfUnneededSince.TryGetValue(chunk, out var since))
        {
            _wfUnneededSince[chunk] = since = now;
        }

        if (now - since < _wfUnloadGrace)
            return false;

        _wfUnneededSince.Remove(chunk);
        return true;
    }
}
