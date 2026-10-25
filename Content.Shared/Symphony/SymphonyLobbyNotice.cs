using Robust.Shared.Serialization;

namespace Content.Shared.Symphony;

/// <summary>
/// The notice staff set from the SSymphony panel for the lobby: a title, a few lines, and how it should look. Sent to
/// each player as they reach the lobby and to everyone when it changes; an empty one takes the box away.
/// </summary>
[Serializable, NetSerializable]
public sealed class SymphonyLobbyNoticeEvent : EntityEventArgs
{
    public string? Title;

    /// <summary>The body, in the game's markup: [bold], [color=...] and line breaks.</summary>
    public string? Body;

    /// <summary>info, warning or event: the accent colour of the box.</summary>
    public string Style = "info";

    public SymphonyLobbyNoticeEvent(string? title, string? body, string style)
    {
        Title = title;
        Body = body;
        Style = style;
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Body);
}
