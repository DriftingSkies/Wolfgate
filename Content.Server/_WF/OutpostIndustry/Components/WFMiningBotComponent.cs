using Robust.Shared.Containers;

namespace Content.Server._WF.OutpostIndustry.Components;

/// <summary>A dock's mining bot: drills ore-bearing rock, carries the ore home, rests at night (<see cref="WFOutpostIndustrySystem"/>).</summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(WFOutpostIndustrySystem))]
public sealed partial class WFMiningBotComponent : Component
{
    public const string HoldId = "wf-mining-bot-hold";

    [ViewVariables]
    public EntityUid Dock;

    [ViewVariables]
    public WFMiningBotStage Stage;

    /// <summary>The rock it is going for or drilling.</summary>
    [ViewVariables]
    public EntityUid? Rock;

    /// <summary>Rocks it couldn't reach, so it picks another.</summary>
    public readonly HashSet<EntityUid> Unreachable = new();

    [DataField]
    public TimeSpan DrillTime = TimeSpan.FromSeconds(3);

    [ViewVariables, AutoPausedField]
    public TimeSpan DrillUntil;

    /// <summary>When a bot that found no rock looks again.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextLook;

    /// <summary>Ore pieces it carries before it goes home.</summary>
    [DataField]
    public int Capacity = 8;

    [ViewVariables]
    public Container Hold = default!;
}

public enum WFMiningBotStage : byte
{
    Seek,
    Drill,
    Return,
    Rest,
}
