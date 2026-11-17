using System.Diagnostics.CodeAnalysis;
using Content.Client._WG.MappingMode.Palette;
using Content.Shared._WG.MappingMode;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.MappingMode.Modes;

public sealed partial class WGMappingBuildModeSystem
{
    /// <summary>Raised when the server sends a new spawner table, so what won't save can be marked again.</summary>
    public event Action? SpawnersChanged;

    private void InitializeSpawners()
    {
        _client.SpawnerIndexUpdated += OnSpawnerIndexUpdated;
    }

    private void ShutdownSpawners()
    {
        _client.SpawnerIndexUpdated -= OnSpawnerIndexUpdated;
    }

    private void OnSpawnerIndexUpdated()
    {
        SpawnersChanged?.Invoke();
    }

    /// <summary>Whether a map file leaves an entity entry out, as it does for every mob.</summary>
    public bool WontSave(WGMappingPaletteEntry entry)
    {
        return entry.Kind == WGMappingPaletteKind.Entity &&
               _prototypes.TryIndex<EntityPrototype>(entry.Id, out var prototype) &&
               WGMappingSpawnerIndex.WontSave(prototype);
    }

    /// <summary>The spawner entry to place instead of one that won't save, if a spawner spawns it.</summary>
    public bool TryGetSpawner(WGMappingPaletteEntry entry, [NotNullWhen(true)] out string? spawnerId)
    {
        spawnerId = null;
        return WontSave(entry) && _client.SpawnerIndex.TryGetSpawner(entry.Id, out spawnerId) &&
               _prototypes.HasIndex<EntityPrototype>(spawnerId);
    }

    /// <summary>The warning for an entity prototype that won't save, or null for one that does.</summary>
    public string? UnsavedNote(string prototypeId)
    {
        var entry = new WGMappingPaletteEntry(WGMappingPaletteKind.Entity, prototypeId);
        if (!WontSave(entry))
            return null;

        var name = NameOf(prototypeId);
        return TryGetSpawner(entry, out var spawnerId)
            ? Loc.GetString("wg-mapping-mode-unsaved-spawner", ("name", name), ("spawner", NameOf(spawnerId)))
            : Loc.GetString("wg-mapping-mode-unsaved-none", ("name", name));
    }

    /// <summary>The one-line hint for the Properties panel, when the selected entry has a spawner to use instead.</summary>
    public string? SpawnerHint(WGMappingPaletteEntry entry)
    {
        return TryGetSpawner(entry, out var spawnerId)
            ? Loc.GetString("wg-mapping-screen-unsaved-hint", ("spawner", NameOf(spawnerId)))
            : null;
    }

    /// <summary>Starts placing the spawner for an entry that won't save.</summary>
    public void UseSpawner(WGMappingPaletteEntry entry)
    {
        if (TryGetSpawner(entry, out var spawnerId))
            BeginPlacing(new WGMappingPaletteEntry(WGMappingPaletteKind.Entity, spawnerId));
    }

    private string NameOf(string prototypeId)
    {
        return _prototypes.TryIndex<EntityPrototype>(prototypeId, out var prototype) && prototype.Name.Length > 0
            ? prototype.Name
            : prototypeId;
    }
}
