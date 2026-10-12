using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Encounters;

/// <summary>Where every visible encounter is, sent to all players for the sector markers on their radars.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterMarkersEvent : EntityEventArgs
{
    public List<WFEncounterMarker> Markers = new();
}

/// <summary>One ship of a visible encounter: where it was at the update, its zones and how to draw it.</summary>
[Serializable, NetSerializable]
public sealed class WFEncounterMarker
{
    /// <summary>The encounter the ship belongs to; its ships share it.</summary>
    public NetEntity Encounter;

    /// <summary>The encounter's name.</summary>
    public string Name = string.Empty;

    /// <summary>The ship's own name.</summary>
    public string Ship = string.Empty;

    /// <summary>The side the ship fights for.</summary>
    public string Side = string.Empty;

    public WFEncounterCategory Category;

    /// <summary>The glyph drawn for it at the rim of the scope; Category for the category's own.</summary>
    public WFEncounterIcon Icon;

    /// <summary>The ship's grid. The radar draws the zones around it where the client knows it.</summary>
    public NetEntity Grid;

    /// <summary>The ship's IFF colour, or its side's colour when the encounter has several sides; none draws by category.</summary>
    public Color? Color;

    public MapId Map;

    /// <summary>The ship's centre of mass in map space when the update was made.</summary>
    public Vector2 Position;

    /// <summary>The centre of mass's velocity.</summary>
    public Vector2 Velocity;

    /// <summary>Radius inside which ships are warned off; zero for none.</summary>
    public float WarnRange;

    /// <summary>Radius inside which ships are fired on; zero for none.</summary>
    public float AttackRange;
}
