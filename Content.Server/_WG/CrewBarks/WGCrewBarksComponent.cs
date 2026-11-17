namespace Content.Server._WG.CrewBarks;

/// <summary>
/// Gives a commanded ship's roster crew and counter staff something to say: thanks for the Boss's blows, greetings by
/// standing, idle thoughts and reactions. Lives on the grid; a vessel opts in through its <c>addComponents</c>.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewBarksComponent : Component
{
    /// <summary>
    /// Voices by entity prototype or crew role id, ahead of the usual ones: a role's name without <c>WGCrew</c>
    /// (<c>marine</c>), <c>radio</c> for the radio operator and <c>trader</c> for counter staff.
    /// </summary>
    [DataField]
    public Dictionary<string, string> Voices = new();

    /// <summary>The least time between two lines from one crewman.</summary>
    [DataField]
    public TimeSpan LineGap = TimeSpan.FromSeconds(20);

    /// <summary>The least time between two thank-yous for the Boss's blows.</summary>
    [DataField]
    public TimeSpan StruckGap = TimeSpan.FromSeconds(3);

    /// <summary>A line keeps everyone else within this many metres quiet for <see cref="AreaGap"/>.</summary>
    [DataField]
    public float AreaRange = 6f;

    /// <summary>How long a line keeps everyone within <see cref="AreaRange"/> quiet.</summary>
    [DataField]
    public TimeSpan AreaGap = TimeSpan.FromSeconds(6);

    /// <summary>How close someone passes to be greeted.</summary>
    [DataField]
    public float GreetRange = 3f;

    /// <summary>How long before a crewman greets the same person again.</summary>
    [DataField]
    public TimeSpan GreetAgain = TimeSpan.FromMinutes(5);

    /// <summary>The chance a guest passing gets a word at all.</summary>
    [DataField]
    public float GuestGreetChance = 0.35f;

    /// <summary>The shortest time between a crewman's idle thoughts.</summary>
    [DataField]
    public TimeSpan IdleMin = TimeSpan.FromMinutes(3);

    /// <summary>The longest time between a crewman's idle thoughts.</summary>
    [DataField]
    public TimeSpan IdleMax = TimeSpan.FromMinutes(8);

    /// <summary>Idle thoughts and jump lines are said only with a player this close.</summary>
    [DataField]
    public float HearRange = 8f;

    /// <summary>Healing of at least this much at once draws a thank-you.</summary>
    [DataField]
    public float HealedAt = 10f;
}
