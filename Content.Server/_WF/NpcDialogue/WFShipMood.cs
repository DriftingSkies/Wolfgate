namespace Content.Server._WF.NpcDialogue;

/// <summary>How an NPC ship rates its own chances, worst last.</summary>
public enum WFShipMood : byte
{
    /// <summary>Whole and at least a match for whoever it faces.</summary>
    Confident,

    /// <summary>Whole, but facing a stronger ship.</summary>
    Wary,

    /// <summary>Taking damage or losing crew.</summary>
    Hurt,

    /// <summary>Badly hurt: it stops making demands and looks for a way out.</summary>
    Desperate,

    /// <summary>Beaten: out of the fight or close to it.</summary>
    Broken,
}

/// <summary>Things a side remembers about a vessel and brings up once each.</summary>
[Flags]
public enum WFTalkFacts : byte
{
    None = 0,

    /// <summary>It came back after leaving the zone.</summary>
    Returned = 1 << 0,

    /// <summary>It ran from a fight with the side.</summary>
    Fled = 1 << 1,

    /// <summary>It fired on the side before the side fired on it.</summary>
    Fired = 1 << 2,

    /// <summary>It knocked one of the side's ships out of the fight.</summary>
    Killed = 1 << 3,

    /// <summary>The side has gloated over its damage.</summary>
    Gloated = 1 << 4,
}

/// <summary>What one side has said to one vessel and still holds against it.</summary>
public sealed class WFTalkState
{
    /// <summary>Warnings given this visit: one, then a final one, then silence.</summary>
    public int Stage;

    /// <summary>When the next warning may come.</summary>
    public TimeSpan NextWarn;

    /// <summary>The last line said, so it is not said twice running.</summary>
    public string? Last;

    /// <summary>What the side has noticed about the vessel.</summary>
    public WFTalkFacts Known;

    /// <summary>What it has already brought up.</summary>
    public WFTalkFacts Said;

    /// <summary>Whether the vessel left the zone since it was last spoken to.</summary>
    public bool Left;

    /// <summary>Whether the side fired on it before it left.</summary>
    public bool WasEngaged;
}
