namespace Content.Server._WF.MappingMode;

/// <summary>What a map mapping mode made is for.</summary>
public enum WFMappingMapKind : byte
{
    /// <summary>A level of a session.</summary>
    Session,

    /// <summary>The private map admin observers start on.</summary>
    Admin,

    /// <summary>Holds a reference file while its mapper places it.</summary>
    Scratch,

    /// <summary>A running level of a session's live test: initialised, never saved, never edited.</summary>
    Test,
}

/// <summary>
/// Tags a map mapping mode made with what it is for and its session. Never saved, so library files don't carry it.
/// </summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFMappingSessionMapComponent : Component
{
    [ViewVariables]
    public WFMappingMapKind Kind;

    /// <summary>The session it belongs to; 0 for the admin map.</summary>
    [ViewVariables]
    public int SessionId;
}
