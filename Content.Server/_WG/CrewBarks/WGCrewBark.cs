namespace Content.Server._WG.CrewBarks;

/// <summary>What a crewman's line is about. Each has its Fluent lines per voice: <c>wg-crew-bark-voice-kind-n</c>.</summary>
public enum WGCrewBark : byte
{
    /// <summary>The Boss hit or shoved him.</summary>
    Struck,

    /// <summary>Anyone else hit or shoved him.</summary>
    Attacked,

    /// <summary>The Boss passed by; said with a salute.</summary>
    GreetBoss,

    /// <summary>Someone on the staff list passed by.</summary>
    GreetStaff,

    /// <summary>A guest passed by; only sometimes.</summary>
    GreetGuest,

    /// <summary>Someone barred from the ship passed by.</summary>
    GreetBarred,

    /// <summary>A thought of his own, now and then, with a player in earshot.</summary>
    Idle,

    /// <summary>Someone patched him up.</summary>
    Healed,

    /// <summary>A player took his post's job; <c>$name</c> is the player.</summary>
    Relieved,

    /// <summary>His post was stood down from the bridge.</summary>
    StoodDown,

    /// <summary>He came aboard at the crew bay and reached his post.</summary>
    Reporting,

    /// <summary>His crew found a hostile he can see.</summary>
    Fight,

    /// <summary>The ship is jumping.</summary>
    Jump,

    /// <summary>The ship came out of a jump.</summary>
    Arrived,
}
