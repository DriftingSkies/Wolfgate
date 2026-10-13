using Content.Shared._WF.Hailing;

namespace Content.Server._WF.Hailing;

/// <summary>
/// Marks the grid of a hailing test ship: a console and a pilot that hail on command, answer and echo.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class HailTestShipComponent : Component
{
    [ViewVariables]
    public EntityUid Console;

    [ViewVariables]
    public EntityUid Operator;

    /// <summary>The ship the pilot has been told to hail and hasn't reached yet.</summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>When the pilot starts trying, and when they stop.</summary>
    [ViewVariables]
    public TimeSpan HailAt;

    [ViewVariables]
    public TimeSpan GiveUpAt;

    /// <summary>When the console last had a hail on it. The ship is removed some minutes after.</summary>
    [ViewVariables]
    public TimeSpan IdleSince;

    /// <summary>The pilot offers three answers to pick from instead of echoing straight away.</summary>
    [ViewVariables]
    public bool Options;

    /// <summary>The answer picked, and when the pilot says so.</summary>
    [ViewVariables]
    public string? Picked;

    [ViewVariables]
    public TimeSpan PickedAt;

    /// <summary>The next line from the other end is the picked answer, which is not echoed.</summary>
    [ViewVariables]
    public bool SkipLine;

    /// <summary>
    /// What the hailing events have told this ship, for checking by hand and in tests: hails answered, calls
    /// ended and how the last one ended, and answers picked.
    /// </summary>
    [ViewVariables]
    public int Answered;

    [ViewVariables]
    public int Ended;

    [ViewVariables]
    public HailEndReason? LastEnd;

    [ViewVariables]
    public bool LastEndConnected;

    [ViewVariables]
    public int Chosen;

    /// <summary>The pilot has said their piece on this call.</summary>
    [ViewVariables]
    public bool Greeted;

    /// <summary>The first line of this call the pilot has not read yet.</summary>
    [ViewVariables]
    public int NextLine;

    /// <summary>The last thing the other end said, and when the pilot answers it.</summary>
    [ViewVariables]
    public string Heard = string.Empty;

    [ViewVariables]
    public TimeSpan? ReplyAt;
}
