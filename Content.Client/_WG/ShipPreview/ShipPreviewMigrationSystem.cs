using System.IO;
using System.Linq;
using Robust.Shared.ContentPack;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Client._WG.ShipPreview;

/// <summary>
/// Applies the entity migration files to grids the client loads itself, as the server's MapMigrationSystem does, so a
/// grid that still uses a renamed or removed prototype previews instead of failing to load.
/// </summary>
public sealed partial class ShipPreviewMigrationSystem : EntitySystem
{
    [Dependency] private IResourceManager _resMan = default!;

    /// <summary>The same files the server reads.</summary>
    private static readonly ResPath[] MigrationFiles =
    {
        new("/migration.yml"),
        new("/nf_migration.yml"),
        new("/mono_migration.yml"),
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BeforeEntityReadEvent>(OnBeforeEntityRead);
    }

    private void OnBeforeEntityRead(BeforeEntityReadEvent ev)
    {
        foreach (var path in MigrationFiles)
        {
            if (!_resMan.TryContentFileRead(path, out var stream))
                continue;

            using var reader = new StreamReader(stream, EncodingHelpers.UTF8);
            if (DataNodeParser.ParseYamlStream(reader).FirstOrDefault()?.Root is not MappingDataNode mapping)
                continue;

            foreach (var (from, node) in mapping)
            {
                if (node is not ValueDataNode value)
                    continue;

                if (string.IsNullOrWhiteSpace(value.Value) || value.Value == "null")
                    ev.DeletedPrototypes.Add(from);
                else
                    ev.RenamedPrototypes[from] = value.Value;
            }
        }
    }
}
