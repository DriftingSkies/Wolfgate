using Robust.Shared.Prototypes;

namespace Content.Shared._WF.NpcDialogue;

/// <summary>How an encounter ship's side talks and gives up: picked by the zone lines its ships use.</summary>
[Prototype("wfNpcVoice")]
public sealed partial class WFNpcVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The zone line prefix this voice speaks for, such as <c>wf-encounter-pirate-zone</c>.</summary>
    [DataField(required: true)]
    public string Lines = string.Empty;

    /// <summary>Chance that a beaten ship offers its surrender, rolled once per ship.</summary>
    [DataField]
    public float Surrender = WFNpcVoice.DefaultSurrender;

    /// <summary>Whether a badly hurt ship runs from its attackers instead of circling them.</summary>
    [DataField]
    public bool Retreats = true;
}

public static class WFNpcVoice
{
    /// <summary>Surrender chance for a ship whose zone lines have no voice.</summary>
    public const float DefaultSurrender = 0.1f;
}
