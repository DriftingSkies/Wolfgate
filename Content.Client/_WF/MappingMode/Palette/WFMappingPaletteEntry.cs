namespace Content.Client._WF.MappingMode.Palette;

/// <summary>What a palette entry places.</summary>
public enum WFMappingPaletteKind : byte
{
    Entity,
    Tile,
    Decal,
}

/// <summary>Something the mapping screen can place: an entity prototype, a tile or a decal with its colour.</summary>
public readonly record struct WFMappingPaletteEntry(WFMappingPaletteKind Kind, string Id, Color? Color = null);
