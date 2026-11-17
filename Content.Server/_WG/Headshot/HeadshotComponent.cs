namespace Content.Server._WG.Headshot;

/// <summary>A character's headshot, from their profile. Server-only so clients can't match hashes to unmask people.</summary>
[RegisterComponent, Access(typeof(HeadshotSystem))]
public sealed partial class HeadshotComponent : Component
{
    [DataField]
    public string Url = string.Empty;

    /// <summary>Hash of the fetched image, or null while it downloads or if it failed.</summary>
    [ViewVariables]
    public string? Hash;
}
