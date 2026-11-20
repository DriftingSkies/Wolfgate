using Robust.Shared.Serialization;

namespace Content.Shared._WG.Ghost;

/// <summary>Sent to every ghost when something worth watching happens: a chip that orbits the source when clicked.</summary>
[Serializable, NetSerializable]
public sealed class GhostNoticeEvent : EntityEventArgs
{
    /// <summary>What to orbit.</summary>
    public NetEntity Source;

    /// <summary>Short heading, such as "Xeno infection".</summary>
    public string Title = string.Empty;

    public string Text = string.Empty;

    /// <summary>The source's prototype, drawn when the client doesn't have the entity.</summary>
    public string? Prototype;

    public Color Color = Color.White;

    /// <summary>Seconds the chip stays up.</summary>
    public float Duration = 30f;
}
