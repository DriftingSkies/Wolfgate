namespace Content.Client._WF.MappingMode.Modes;

/// <summary>The mapping screen's build modes, in the order of their number keys.</summary>
public enum WFMappingBuildMode : byte
{
    Place = 1,
    Erase,
    Select,
    Fill,
    Shape,
    Pick,
    Template,
    Pipe,
    Link,
}

/// <summary>What Erase mode removes.</summary>
public enum WFMappingEraseTarget : byte
{
    Entity,
    Tile,
    Decal,

    /// <summary>Entities, decals and tiles together.</summary>
    Everything,
}

/// <summary>What Shape mode draws.</summary>
public enum WFMappingShapeKind : byte
{
    Line,
    Rect,

    /// <summary>The ellipse inscribed in the dragged box.</summary>
    Circle,
}

/// <summary>Whether a rectangle or circle is drawn whole or only its border.</summary>
public enum WFMappingShapeFill : byte
{
    Fill,
    Border,
}

/// <summary>The colour Pipe mode paints with.</summary>
public enum WFMappingPipePreset : byte
{
    Distro,
    Waste,
    Air,
    Mix,

    /// <summary>The colour typed into Properties.</summary>
    Custom,

    /// <summary>The colour of the first pipe the path joins, else none.</summary>
    Auto,
}

/// <summary>What Link mode configures.</summary>
public enum WFMappingLinkTool : byte
{
    /// <summary>Signal links from a source's ports to a sink's.</summary>
    Signals,

    /// <summary>The devices in a device list, such as an air alarm's.</summary>
    Devices,

    /// <summary>The access levels a reader accepts.</summary>
    Access,
}
