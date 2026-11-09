using Content.Shared._WF.UploadCache;

namespace Content.Client._WF.UploadCache;

/// <summary>Takes the server's list of shared files and asks for the ones this client has not cached.</summary>
public sealed partial class WFUploadCacheSystem : EntitySystem
{
    [Dependency] private WFUploadCacheManager _cache = default!;

    public override void Initialize()
    {
        base.Initialize();
        _cache.Initialize();
        SubscribeNetworkEvent<WFUploadManifestEvent>(OnManifest);
        SubscribeNetworkEvent<WFUploadFilesEvent>(OnFiles);
    }

    private void OnFiles(WFUploadFilesEvent ev)
    {
        for (var i = 0; i < ev.Hashes.Count && i < ev.Data.Count; i++)
        {
            _cache.Receive(ev.Hashes[i], ev.Data[i]);
        }
    }

    private void OnManifest(WFUploadManifestEvent ev)
    {
        var wanted = _cache.Apply(ev);
        if (wanted.Count > 0)
            RaiseNetworkEvent(new WFUploadRequestEvent { Hashes = wanted });
    }
}
