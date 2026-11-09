using Content.Shared._WF.MappingTools;
using Content.Shared.Construction.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Lock;
using Content.Shared.Tools.Components;
using Content.Shared.Wires;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Modes;

public sealed partial class WFMappingBuildModeSystem
{
    /// <summary>The tool a click in Tool mode uses.</summary>
    public WFMappingTool Tool { get; private set; } = WFMappingTool.Wrench;

    public void SetTool(WFMappingTool tool)
    {
        Tool = tool;
        SettingsChanged?.Invoke();
    }

    /// <summary>The server toggle a tool sends, or null for the wrench.</summary>
    public static MappingToggle? ToggleOf(WFMappingTool tool)
    {
        return tool == WFMappingTool.Wrench ? null : (MappingToggle) (tool - 1);
    }

    /// <summary>Whether the current tool works on an entity, and the state it reads there.</summary>
    public bool TryToolState(EntityUid uid, out bool on)
    {
        on = false;
        switch (Tool)
        {
            case WFMappingTool.Wrench when HasComp<AnchorableComponent>(uid):
                on = Transform(uid).Anchored;
                return true;
            case WFMappingTool.Bolts when TryComp(uid, out DoorBoltComponent? bolts):
                on = bolts.BoltsDown;
                return true;
            case WFMappingTool.Weld when TryComp(uid, out WeldableComponent? weld):
                on = weld.IsWelded;
                return true;
            case WFMappingTool.Lock when TryComp(uid, out LockComponent? lockComp):
                on = lockComp.Locked;
                return true;
            case WFMappingTool.Panel when TryComp(uid, out WiresPanelComponent? panel):
                on = panel.Open;
                return true;
            case WFMappingTool.Door when TryComp(uid, out DoorComponent? door):
                on = door.State is DoorState.Open or DoorState.Opening;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The entity at a map position the current tool works on, anchored ones first.</summary>
    public EntityUid? ToolTargetAt(MapCoordinates position)
    {
        if (Tool == WFMappingTool.Wrench)
            return AnchorableAt(position);

        if (position.MapId == MapId.Nullspace)
            return null;

        EntityUid? found = null;
        foreach (var uid in _lookup.GetEntitiesIntersecting(position))
        {
            if (!TryToolState(uid, out _))
                continue;

            if (Transform(uid).Anchored)
                return uid;

            found ??= uid;
        }

        return found;
    }

    /// <summary>What a click with the current tool would change, or how to use it.</summary>
    private string ToolReadout()
    {
        var tool = Tool.ToString().ToLowerInvariant();
        if (ToolTargetAt(MouseMapPosition) is not { } target || !TryToolState(target, out var on))
            return Loc.GetString($"wf-mapping-screen-readout-tool-{tool}");

        return Loc.GetString("wf-mapping-screen-readout-tool-target",
            ("tool", Loc.GetString($"wf-mapping-tool-{tool}")),
            ("name", Name(target)),
            ("state", Loc.GetString($"wf-mapping-tool-state-{tool}", ("on", on ? "yes" : "no"))));
    }
}
