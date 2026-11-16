namespace Content.Shared._WF.Modsuits.Modules;

/// <summary>Raised on a suit part when an EMP tries to reach something inside it.</summary>
[ByRefEvent]
public record struct ContainedEmpAttemptEvent(EntityUid Target)
{
    public bool Cancelled;
}

/// <summary>Asks a suit module whether a language may go out over radio.</summary>
[ByRefEvent]
public record struct LanguageRadioAttemptEvent(string Language)
{
    public bool Allowed;
    public string? TranslateTo;
}

/// <summary>Raised on a projectile after a player fires it.</summary>
[ByRefEvent]
public record struct PlayerShotProjectileEvent(EntityUid Projectile, EntityUid User);
