using Content.Shared._WG.UploadCache;

namespace Content.Client._WG.UploadCache;

/// <summary>Takes the server's list of shared files and asks for the ones this client has not cached.</summary>
public sealed partial class WGUploadCacheSystem : EntitySystem
{
    [Dependency] private WGUploadCacheManager _cache = default!;

    public override void Initialize()
    {
        base.Initialize();
        _cache.Initialize();
        SubscribeNetworkEvent<WGUploadManifestEvent>(OnManifest);
        SubscribeNetworkEvent<WGUploadFilesEvent>(OnFiles);
    }

    private void OnFiles(WGUploadFilesEvent ev)
    {
        for (var i = 0; i < ev.Hashes.Count && i < ev.Data.Count; i++)
        {
            _cache.Receive(ev.Hashes[i], ev.Data[i]);
        }
    }

    private void OnManifest(WGUploadManifestEvent ev)
    {
        var wanted = _cache.Apply(ev);
        if (wanted.Count > 0)
            RaiseNetworkEvent(new WGUploadRequestEvent { Hashes = wanted });
    }
}
