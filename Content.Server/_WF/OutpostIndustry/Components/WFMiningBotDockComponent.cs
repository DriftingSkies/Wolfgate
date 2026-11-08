using Robust.Shared.Prototypes;

namespace Content.Server._WF.OutpostIndustry.Components;

/// <summary>Houses mining bots: it makes them, they mine ore-bearing rock round it by day, and it stores what they bring.</summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(WFOutpostIndustrySystem))]
public sealed partial class WFMiningBotDockComponent : Component
{
    [DataField]
    public int Bots = 2;

    [DataField]
    public EntProtoId BotPrototype = "WFMobMiningBot";

    /// <summary>How far from the dock its bots look for rock.</summary>
    [DataField]
    public float Range = 24f;

    /// <summary>The bots it made. Mobs aren't saved, so a loaded dock makes them again.</summary>
    [ViewVariables]
    public List<EntityUid> Spawned = new();

    /// <summary>The least time between two bots it makes, so a destroyed one isn't back at once.</summary>
    [DataField]
    public TimeSpan Rebuild = TimeSpan.FromMinutes(5);

    [ViewVariables, AutoPausedField]
    public TimeSpan NextBot;
}
