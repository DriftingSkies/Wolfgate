using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.CodaSong;

/// <summary>
/// A piece of the song the Coda sings in a drifting mind, split into one stem per part so the client can bring voices
/// in and turn them one by one as Drift rises.
/// </summary>
[Prototype("wgCodaSong")]
public sealed partial class WGCodaSongPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>How long the piece runs, in seconds.</summary>
    [DataField(required: true)]
    public float Seconds;

    /// <summary>Each part's clean stem, by part name, lowest voice last.</summary>
    [DataField(required: true)]
    public Dictionary<string, ResPath> Stems = new();

    /// <summary>Each part's turned stem; a part without one never turns.</summary>
    [DataField]
    public Dictionary<string, ResPath> Turned = new();

    /// <summary>The Drift range, 0 to 100, it can play in.</summary>
    [DataField]
    public float MinDrift;

    [DataField]
    public float MaxDrift = 100f;

    /// <summary>How likely it is to be picked among the pieces that fit.</summary>
    [DataField]
    public float Weight = 1f;

    /// <summary>Which singing things it plays for, besides a drifting mind: e.g. "fragment" for a fragment's heart.</summary>
    [DataField]
    public string? Source;
}

/// <summary>Something that sings the Coda's song to anyone near it, whatever their Drift: a fragment's heart.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGSongSourceComponent : Component
{
    /// <summary>Which pieces it sings: those whose <see cref="WGCodaSongPrototype.Source"/> matches.</summary>
    [DataField(required: true), AutoNetworkedField]
    public string Source = string.Empty;

    /// <summary>How far it is heard, in metres.</summary>
    [DataField, AutoNetworkedField]
    public float Range = 60f;

    /// <summary>Damage at which its singers have fully turned; 0 if harm doesn't turn them.</summary>
    [DataField]
    public float TurnedAt;

    /// <summary>How far its singers have turned, 0 to 1.</summary>
    [DataField, AutoNetworkedField]
    public float Turned;

    /// <summary>Gone quiet for good.</summary>
    [DataField, AutoNetworkedField]
    public bool Silent;
}

/// <summary>
/// On a player: the singing thing within reach of them, if any, worked out by the server so a heart is heard from its
/// whole range and not only once the client can see it. Sent only to its owner.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGSongListenerComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>Which pieces the nearest source sings; null when none is in reach.</summary>
    [AutoNetworkedField]
    public string? Source;

    /// <summary>How near it is, 0.25 at the edge of its reach to 1 beside it.</summary>
    [AutoNetworkedField]
    public float Near;

    /// <summary>How far its singers have turned, 0 to 1.</summary>
    [AutoNetworkedField]
    public float Turned;
}
