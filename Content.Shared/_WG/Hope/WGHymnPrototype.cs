using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Hope;

/// <summary>One hymn a hymn source sings: a whole piece, played to anyone within the source's reach.</summary>
[Prototype("wgHymn")]
public sealed partial class WGHymnPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The piece's audio.</summary>
    [DataField(required: true)]
    public ResPath Path;

    /// <summary>How long the piece runs, in seconds.</summary>
    [DataField(required: true)]
    public float Seconds;

    /// <summary>Which sources sing it: those whose <see cref="WGHymnSourceComponent.Source"/> matches.</summary>
    [DataField(required: true)]
    public string Source = string.Empty;

    /// <summary>How likely it is to be picked among the source's hymns.</summary>
    [DataField]
    public float Weight = 1f;
}

/// <summary>Something that sings hymns to anyone near it, such as an altar to the Sultan.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGHymnSourceComponent : Component
{
    /// <summary>Which hymns it sings: those whose <see cref="WGHymnPrototype.Source"/> matches.</summary>
    [DataField(required: true), AutoNetworkedField]
    public string Source = string.Empty;

    /// <summary>How far it is heard, in metres.</summary>
    [DataField, AutoNetworkedField]
    public float Range = 18f;

    /// <summary>Seconds of quiet between one hymn and the next, at least and at most.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Gap = new(8f, 20f);

    /// <summary>Gone quiet, for an altar that has been desecrated or switched off.</summary>
    [DataField, AutoNetworkedField]
    public bool Silent;
}

/// <summary>
/// On a player: the hymn source within reach of them, if any, worked out by the server so a source is heard from its
/// whole reach and not only once the client can see it. Sent only to its owner.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGHymnListenerComponent : Component
{
    public override bool SendOnlyToOwner => true;

    /// <summary>Which hymns the nearest source sings; null when none is in reach.</summary>
    [AutoNetworkedField]
    public string? Source;

    /// <summary>How near it is, 0.25 at the edge of its reach to 1 beside it.</summary>
    [AutoNetworkedField]
    public float Near;

    /// <summary>The nearest source's quiet between hymns, at least and at most, in seconds.</summary>
    [AutoNetworkedField]
    public Vector2 Gap;
}
