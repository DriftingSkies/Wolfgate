using Content.Shared._WG.Performance;

namespace Content.Client._Mono.Radar;

public sealed partial class RadarBlipsSystem
{
    private readonly HashSet<NetEntity> _wgRemoved = new();

    /// <summary>Drops every blip the server removed this tick.</summary>
    private void WGRemoveBlips(WGBlipRemovalBatchEvent args)
    {
        _wgRemoved.Clear();
        _wgRemoved.UnionWith(args.Blips);
        _blips.RemoveAll(blip => _wgRemoved.Contains(blip.Uid));
    }
}
