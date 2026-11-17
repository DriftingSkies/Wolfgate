using Robust.Shared.Audio;

namespace Content.Server._WG.Remade.Elites;

/// <summary>The Undying. Its arrival is announced to everyone on its grid.</summary>
[RegisterComponent]
public sealed partial class WGUndyingComponent : Component
{
    [DataField]
    public SoundSpecifier Arrival = new SoundCollectionSpecifier("WGRemadeUndyingUbermorphSpawn");

    [DataField]
    public float Volume = -2f;

    /// <summary>The people who heard it arrive.</summary>
    [ViewVariables]
    public HashSet<EntityUid> HeardBy = new();
}
