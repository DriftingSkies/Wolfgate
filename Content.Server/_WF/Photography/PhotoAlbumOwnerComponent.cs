namespace Content.Server._WF.Photography;

/// <summary>A personal album that is saved through the round and comes back with the character it belongs to.</summary>
[RegisterComponent]
public sealed partial class PhotoAlbumOwnerComponent : Component
{
    /// <summary>Identifies the album's saved copy. It stays with the album when restored or put in storage.</summary>
    [DataField]
    public string AlbumId = string.Empty;

    /// <summary>Database ID of the character profile that owns the album.</summary>
    [DataField]
    public int ProfileId;

    /// <summary>Whether the album changed since it was last saved.</summary>
    [ViewVariables]
    public bool Changed;
}
