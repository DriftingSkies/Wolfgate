using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server._WG.Prototypes;

/// <summary>Rewrites the maps saved in the server's data folder off the old WF prefix, once, at startup.</summary>
public sealed class WGLegacyMapFilesSystem : EntitySystem
{
    [Dependency] private IResourceManager _res = default!;

    /// <summary>Written after a full pass, so later starts skip it.</summary>
    public static readonly ResPath Stamp = new("/wg_prefix_maps.done");

    private const int MaxDepth = 8;

    public override void Initialize()
    {
        base.Initialize();

        if (_res.UserData.Exists(Stamp))
            return;

        var (converted, failed) = ConvertFolder(ResPath.Root, 0);
        if (converted > 0 || failed > 0)
            Log.Info($"Moved {converted} saved maps to the WG prefix, {failed} could not be read or written.");

        // A failed file is retried on the next start.
        if (failed == 0)
            _res.UserData.WriteAllText(Stamp, string.Empty);
    }

    /// <summary>Converts every .yml file under a data folder; the counts of files rewritten and files that failed.</summary>
    public (int Converted, int Failed) ConvertFolder(ResPath folder, int depth)
    {
        var converted = 0;
        var failed = 0;
        foreach (var entry in _res.UserData.DirectoryEntries(folder))
        {
            var path = folder / entry;
            if (_res.UserData.IsDir(path))
            {
                if (depth < MaxDepth)
                {
                    var (inner, innerFailed) = ConvertFolder(path, depth + 1);
                    converted += inner;
                    failed += innerFailed;
                }

                continue;
            }

            if (path.Extension != "yml")
                continue;

            try
            {
                var text = _res.UserData.ReadAllText(path);
                var current = WGLegacyMapText.Convert(text);
                if (ReferenceEquals(current, text))
                    continue;

                _res.UserData.WriteAllText(path, current);
                converted++;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                Log.Warning($"Could not move saved map {path} to the WG prefix: {e.Message}");
                failed++;
            }
        }

        return (converted, failed);
    }
}
