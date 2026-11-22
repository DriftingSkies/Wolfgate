namespace Content.Shared._WG.Augments;

/// <summary>
/// An adrenal booster's second wind: when a stamina stun puts the body down, it is back up after
/// <see cref="DownTime"/> with its stamina restored, once per <see cref="Cooldown"/>. A pain shock's adrenaline
/// lasts <see cref="PainShockAdrenaline"/> times as long. It also raises stamina, except on a body with Lethargy.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGAdrenalBoosterComponent : Component
{
    /// <summary>Added to the stamina crit threshold.</summary>
    [DataField]
    public float StaminaBonus = 10f;

    /// <summary>Added to stamina regeneration per second.</summary>
    [DataField]
    public float RegenerationBonus = 0.6f;

    /// <summary>What the wait before stamina regenerates is multiplied by.</summary>
    [DataField]
    public float CooldownMultiplier = 0.75f;

    /// <summary>Whether the stamina bonus is on the body now.</summary>
    [ViewVariables]
    public bool StaminaApplied;

    /// <summary>How long a stamina stun keeps the body down before the surge.</summary>
    [DataField]
    public TimeSpan DownTime = TimeSpan.FromSeconds(1.5);

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    [DataField]
    public float PainShockAdrenaline = 2f;

    /// <summary>When the pending surge stands the body up, if a stun has set one off.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan? SurgeAt;

    [ViewVariables, AutoPausedField]
    public TimeSpan ReadyAt;

    /// <summary>Whether the body is told when the booster is ready again.</summary>
    [ViewVariables]
    public bool Spent;
}
