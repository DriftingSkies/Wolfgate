using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.WolfChem.Narcotics;

/// <summary>
/// The health analyzer's conditions readout: drug levels, status effects something is holding, crashes and
/// dependence, each with what clears it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGAnalyzerConditions
{
    public List<WGAnalyzerDrug> Drugs = new();
    public List<WGAnalyzerHeld> Held = new();
    public List<WGAnalyzerCrash> Crashes = new();
    public List<WGAnalyzerHabit> Habits = new();
}

/// <summary>
/// A reagent behind an effect: how much is in the body, about how long it takes to clear (negative when nothing
/// processes it), and the reagents that remove it faster.
/// </summary>
[Serializable, NetSerializable]
public sealed record WGAnalyzerCause(string Reagent, FixedPoint2 Quantity, float ClearSeconds, bool Stomach,
    List<string> RemovedBy);

/// <summary>A drug level, the reagents raising it and the antidotes that bring its family down.</summary>
[Serializable, NetSerializable]
public sealed record WGAnalyzerDrug(WGDrugFamily Family, WGDrugPhase Phase, List<WGAnalyzerCause> Causes,
    List<string> Antidotes);

/// <summary>What keeps a status effect going besides a reagent.</summary>
[Serializable, NetSerializable]
public enum WGAnalyzerSourceKind : byte
{
    High,
    Crash,
    Withdrawal,
}

[Serializable, NetSerializable]
public readonly record struct WGAnalyzerSource(WGAnalyzerSourceKind Kind, WGDrugFamily Family);

/// <summary>A status effect, its time left, and the reagents and conditions holding it.</summary>
[Serializable, NetSerializable]
public sealed record WGAnalyzerHeld(string Key, float SecondsLeft, List<WGAnalyzerCause> Causes,
    List<WGAnalyzerSource> Sources);

/// <summary>A running crash and its time left.</summary>
[Serializable, NetSerializable]
public readonly record struct WGAnalyzerCrash(WGDrugFamily Family, float SecondsLeft);

/// <summary>
/// Dependence on one family. <see cref="WithdrawalIn"/> is the time until withdrawal without a dose, and
/// <see cref="CleanIn"/> the time clean until the habit is too weak for withdrawal; each is negative when it
/// doesn't apply, and <see cref="CleanIn"/> is infinite when a trait keeps the habit up.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WGAnalyzerHabit(WGDrugFamily Family, float Meter, float Floor, float Tolerance,
    int Stage, float WithdrawalIn, float CleanIn);
