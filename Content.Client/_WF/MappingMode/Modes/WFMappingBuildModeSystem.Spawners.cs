using System.Diagnostics.CodeAnalysis;
using Content.Client._WF.MappingMode.Palette;
using Content.Shared._WF.MappingMode;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.MappingMode.Modes;

public sealed partial class WFMappingBuildModeSystem
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
    public bool WontSave(WFMappingPaletteEntry entry)
    {
        return entry.Kind == WFMappingPaletteKind.Entity &&
               _prototypes.TryIndex<EntityPrototype>(entry.Id, out var prototype) &&
               WFMappingSpawnerIndex.WontSave(prototype);
    }

    /// <summary>The spawner entry to place instead of one that won't save, if a spawner spawns it.</summary>
    public bool TryGetSpawner(WFMappingPaletteEntry entry, [NotNullWhen(true)] out string? spawnerId)
    {
        spawnerId = null;
        return WontSave(entry) && _client.SpawnerIndex.TryGetSpawner(entry.Id, out spawnerId) &&
               _prototypes.HasIndex<EntityPrototype>(spawnerId);
    }

    /// <summary>The warning for an entity prototype that won't save, or null for one that does.</summary>
    public string? UnsavedNote(string prototypeId)
    {
        var entry = new WFMappingPaletteEntry(WFMappingPaletteKind.Entity, prototypeId);
        if (!WontSave(entry))
            return null;

        var name = NameOf(prototypeId);
        return TryGetSpawner(entry, out var spawnerId)
            ? Loc.GetString("wf-mapping-mode-unsaved-spawner", ("name", name), ("spawner", NameOf(spawnerId)))
            : Loc.GetString("wf-mapping-mode-unsaved-none", ("name", name));
    }

    /// <summary>The one-line hint for the Properties panel, when the selected entry has a spawner to use instead.</summary>
    public string? SpawnerHint(WFMappingPaletteEntry entry)
    {
        return TryGetSpawner(entry, out var spawnerId)
            ? Loc.GetString("wf-mapping-screen-unsaved-hint", ("spawner", NameOf(spawnerId)))
            : null;
    }

    /// <summary>Starts placing the spawner for an entry that won't save.</summary>
    public void UseSpawner(WFMappingPaletteEntry entry)
    {
        if (TryGetSpawner(entry, out var spawnerId))
            BeginPlacing(new WFMappingPaletteEntry(WFMappingPaletteKind.Entity, spawnerId));
    }

    private string NameOf(string prototypeId)
    {
        return _prototypes.TryIndex<EntityPrototype>(prototypeId, out var prototype) && prototype.Name.Length > 0
            ? prototype.Name
            : prototypeId;
    }
}
