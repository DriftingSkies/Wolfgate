using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;

namespace Content.Client._WF.MappingMode.Modes;

public sealed partial class WFMappingBuildModeSystem
{
    /// <summary>The atmos device Pipe mode places on a left click, one of <see cref="MappingToolsPipeDevices.Ids"/>; null to draw paths.</summary>
    public string? PipeDevice { get; private set; }

    /// <summary>The side a lone half opens to and a device's first port faces, when nothing around them decides.</summary>
    public PipeDirection PipeFacing { get; private set; } = PipeDirection.North;

    /// <summary>Arms a device for Pipe mode's left click, or goes back to drawing paths with null.</summary>
    public void SetPipeDevice(string? id)
    {
        if (id != null && !MappingToolsPipeDevices.Contains(id))
            return;

        if (PipeDevice == id)
            return;

        PipeDevice = id;
        if (id != null)
            SetPipePainting(false);

        SettingsChanged?.Invoke();
    }

    /// <summary>Turns <see cref="PipeFacing"/> by quarter turns, clockwise for positive steps.</summary>
    public void RotatePipeFacing(int steps)
    {
        var index = Array.IndexOf(MappingToolsPipes.Sides, PipeFacing);
        var count = MappingToolsPipes.Sides.Length;
        PipeFacing = MappingToolsPipes.Sides[((Math.Max(index, 0) + steps) % count + count) % count];
        SettingsChanged?.Invoke();
    }

    /// <summary>The facing's name for the readout and Properties.</summary>
    public string PipeFacingName()
    {
        return Loc.GetString($"wf-mapping-screen-pipe-facing-{PipeFacing.ToString().ToLowerInvariant()}");
    }
}
