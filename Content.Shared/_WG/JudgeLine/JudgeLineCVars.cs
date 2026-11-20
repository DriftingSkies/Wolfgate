using Robust.Shared.Configuration;

namespace Content.Shared._WG.JudgeLine;

/// <summary>Settings for the Judge Line, the vagrants' emergency call.</summary>
[CVarDefs]
public sealed class JudgeLineCVars
{
    /// <summary>Whether vagrants can call the judges.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wg.judgeline.enabled", true, CVar.SERVERONLY);

    /// <summary>What a call costs, taken when it is accepted.</summary>
    public static readonly CVarDef<int> Fee =
        CVarDef.Create("wg.judgeline.fee", 50000, CVar.SERVERONLY);

    /// <summary>How many responders fly at once; further calls wait their turn.</summary>
    public static readonly CVarDef<int> Responders =
        CVarDef.Create("wg.judgeline.responders", 2, CVar.SERVERONLY);

    /// <summary>Seconds from dispatch until the responder drops out of FTL near the caller.</summary>
    public static readonly CVarDef<float> DispatchDelay =
        CVarDef.Create("wg.judgeline.dispatch_delay", 90f, CVar.SERVERONLY);

    /// <summary>How far from the caller, in metres, the responder drops out of FTL.</summary>
    public static readonly CVarDef<float> ArrivalDistance =
        CVarDef.Create("wg.judgeline.arrival_distance", 1000f, CVar.SERVERONLY);

    /// <summary>Minutes a caller waits after a call the judges found no emergency for.</summary>
    public static readonly CVarDef<float> HoaxCooldown =
        CVarDef.Create("wg.judgeline.hoax_cooldown", 30f, CVar.SERVERONLY);

    /// <summary>Minutes a caller waits between calls.</summary>
    public static readonly CVarDef<float> Cooldown =
        CVarDef.Create("wg.judgeline.cooldown", 5f, CVar.SERVERONLY);
}
