namespace Content.Shared._WG.Lattice;

/// <summary>
/// Raised on the server for text a character is about to put down in writing: paper, a label, a PDA note, a message,
/// an announcement. A handler may change what gets written.
/// </summary>
[ByRefEvent]
public record struct WGWrittenTextEvent(EntityUid Writer, string Text)
{
    /// <summary>The text as the writer manages to put it down. Nothing listens on the client, which gets it back unchanged.</summary>
    public static string Apply(IEntityManager entities, EntityUid writer, string text)
    {
        var ev = new WGWrittenTextEvent(writer, text);
        entities.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return ev.Text;
    }
}
