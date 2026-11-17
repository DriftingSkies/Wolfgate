namespace Content.Client._WG.MappingMode.Palette;

/// <summary>What a palette entry places.</summary>
public enum WGMappingPaletteKind : byte
{
    Entity,
    Tile,
    Decal,
}

/// <summary>Something the mapping screen can place: an entity prototype, a tile or a decal with its colour.</summary>
public readonly record struct WGMappingPaletteEntry(WGMappingPaletteKind Kind, string Id, Color? Color = null);
