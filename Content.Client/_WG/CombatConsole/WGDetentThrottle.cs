namespace Content.Client._WG.CombatConsole;

/// <summary>Spaces mechanical detents without queuing delayed clicks.</summary>
public sealed class WGDetentThrottle
{
    private TimeSpan _next;

    /// <summary>Allows a fresh click every 150 milliseconds while the player is adjusting a control.</summary>
    public bool TryPlay(TimeSpan now)
    {
        if (now < _next)
            return false;
        _next = now + TimeSpan.FromMilliseconds(150);
        return true;
    }
}
