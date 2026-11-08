namespace Content.Server._WF.Fauna;

/// <summary>
/// Raised on a <see cref="FaunaCorpseEaterComponent"/> mob when it finishes a meal, before the meal is deleted.
/// </summary>
[ByRefEvent]
public readonly record struct FaunaAteEvent(EntityUid Meal, bool Food);
