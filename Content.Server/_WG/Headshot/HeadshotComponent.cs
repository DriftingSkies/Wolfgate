namespace Content.Server._WG.Headshot;

/// <summary>A character's headshots, from their profile. Server-only so clients can't match hashes to unmask people.</summary>
[RegisterComponent, Access(typeof(HeadshotSystem))]
public sealed partial class HeadshotComponent : Component
{
    /// <summary>The headshot shown on examine; empty for none.</summary>
    [DataField]
    public string Url = string.Empty;

    /// <summary>Hash of the fetched image, or null while it downloads or if it failed.</summary>
    [ViewVariables]
    public string? Hash;

    /// <summary>The adult headshot, shown beside the adult description on the examine card; empty for none.</summary>
    [DataField]
    public string NsfwUrl = string.Empty;

    /// <summary>Hash of the fetched adult headshot, or null while it downloads or if it failed.</summary>
    [ViewVariables]
    public string? NsfwHash;
}
