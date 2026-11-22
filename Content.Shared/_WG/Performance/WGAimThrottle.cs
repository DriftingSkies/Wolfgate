using System.Numerics;

namespace Content.Shared._WG.Performance;

/// <summary>
/// When a held laser pointer's aim is worth sending to the server again, and when its line has moved enough to send to
/// every client. The client sent one every tick for every held pointer, and each dirtied a component every player gets.
/// </summary>
public static class WGAimThrottle
{
    /// <summary>Least time between two aims a client sends for one pointer.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(0.1);

    /// <summary>Most time between aims for a wielded pointer. The server falls back to the holder's facing after 0.5 s.</summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(0.25);

    /// <summary>Least time between two aims the server takes for one pointer, whatever a client sends.</summary>
    public static readonly TimeSpan ServerMinInterval = TimeSpan.FromSeconds(0.05);

    /// <summary>Cosine of the smallest turn worth sending: 2 degrees.</summary>
    private const float MinTurnCos = 0.99939f;

    /// <summary>Square of the smallest move of the holder, or of a line's end, worth sending: 10 cm and 5 cm.</summary>
    private const float MinMoveSquared = 0.01f;
    private const float MinLineMoveSquared = 0.0025f;

    /// <summary>What a client last sent for one pointer.</summary>
    public readonly record struct Sent(EntityUid? Hovered, Vector2? Dir, Vector2 From, TimeSpan At);

    /// <summary>Whether the aim should be sent now, given what was last sent for this pointer.</summary>
    public static bool Due(Sent? last, EntityUid? hovered, Vector2? dir, Vector2 from, TimeSpan now)
    {
        if (last is not { } sent)
            return true;

        var since = now - sent.At;
        if (since < MinInterval)
            return false;

        return since >= KeepAlive
               || sent.Hovered != hovered
               || Turned(sent.Dir, dir)
               || (from - sent.From).LengthSquared() > MinMoveSquared;
    }

    /// <summary>Whether a line has moved enough to be worth sending to every client.</summary>
    public static bool LineMoved(Vector2 oldStart, Vector2 oldEnd, Vector2 start, Vector2 end)
    {
        return (oldStart - start).LengthSquared() > MinLineMoveSquared || (oldEnd - end).LengthSquared() > MinLineMoveSquared;
    }

    private static bool Turned(Vector2? before, Vector2? after)
    {
        if (before is not { } a || after is not { } b)
            return before.HasValue != after.HasValue;

        // A pointer on the holder has no direction to compare; any change from or to that counts.
        var lengths = a.Length() * b.Length();
        if (lengths < 1e-6f)
            return a != b;

        return Vector2.Dot(a, b) / lengths < MinTurnCos;
    }
}
