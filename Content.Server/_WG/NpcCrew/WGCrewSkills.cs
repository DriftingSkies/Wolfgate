using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.NpcCrew;

namespace Content.Server._WG.NpcCrew;

/// <summary>
/// What a crew skill level means in numbers and in tactics. NPC crew are meant to lose to a player of equal kit who
/// moves and spends ammunition: their handicaps are the ones a player can see (stray aim and softer hits; they
/// fire the moment a gun is up), and how much they can take is left to the armour they wear.
/// </summary>
public static class WGCrewSkills
{
    /// <summary>The numbers and tactics of one skill level.</summary>
    public readonly record struct Profile
    {
        /// <summary>Degrees a hand weapon shot may stray to either side.</summary>
        public float AimError { get; init; }

        /// <summary>Seconds with a target in sight before the first shot.</summary>
        public float ShootDelay { get; init; }

        /// <summary>Seconds before firing on a target seen moments ago or braced for.</summary>
        public float ReacquireDelay { get; init; }

        /// <summary>Metres ship guns may be laid off the target, re-rolled every few seconds.</summary>
        public float GunneryError { get; init; }

        /// <summary>How well ship guns lead a moving target, 0 to 1.</summary>
        public float Leading { get; init; }

        /// <summary>Multiplier on thrust and turn rate at the helm.</summary>
        public float Handling { get; init; }

        /// <summary>Multiplier on how far ahead the pilot looks for collisions and incoming fire.</summary>
        public float Evasion { get; init; }

        /// <summary>Whether the pilot steers out of the way of ship weapon fire.</summary>
        public bool DodgesFire { get; init; }

        /// <summary>Multiplier on damage the crewman takes.</summary>
        public float DamageTaken { get; init; }

        /// <summary>Multiplier on damage the crewman does to anyone who is not NPC crew.</summary>
        public float DamageDealt { get; init; }

        /// <summary>Whether he covers the approach to a hostile he knows of but cannot see, instead of going back to work.</summary>
        public bool Braces { get; init; }

        /// <summary>Whether he throws the grenades he carries.</summary>
        public bool Throws { get; init; }

        /// <summary>Seconds he holds an armed grenade before throwing it.</summary>
        public float CookTime { get; init; }

        /// <summary>Whether he steps to a neighbouring tile between shots.</summary>
        public bool Sidesteps { get; init; }

        /// <summary>Whether, badly hurt in a fight, he breaks contact to patch himself up.</summary>
        public bool FallsBack { get; init; }

        /// <summary>How many crewmen go looking together for a hostile nobody can see.</summary>
        public int HuntParty { get; init; }
    }

    private static readonly Profile Green = new()
    {
        AimError = 18f, ShootDelay = 0f, ReacquireDelay = 0f, GunneryError = 30f, Leading = 0.3f, Handling = 0.7f,
        Evasion = 0.4f, DodgesFire = false, DamageTaken = 1.3f, DamageDealt = 0.35f,
        Braces = false, Throws = false, CookTime = 0f, Sidesteps = false, FallsBack = false, HuntParty = 1,
    };

    private static readonly Profile Regular = new()
    {
        AimError = 9f, ShootDelay = 0f, ReacquireDelay = 0f, GunneryError = 14f, Leading = 0.7f, Handling = 0.9f,
        Evasion = 0.75f, DodgesFire = true, DamageTaken = 1.15f, DamageDealt = 0.46f,
        Braces = true, Throws = true, CookTime = 0f, Sidesteps = false, FallsBack = false, HuntParty = 1,
    };

    private static readonly Profile Veteran = new()
    {
        AimError = 4f, ShootDelay = 0f, ReacquireDelay = 0f, GunneryError = 5f, Leading = 1f, Handling = 1f,
        Evasion = 1f, DodgesFire = true, DamageTaken = 1f, DamageDealt = 0.63f,
        Braces = true, Throws = true, CookTime = 1f, Sidesteps = true, FallsBack = true, HuntParty = 2,
    };

    private static readonly Profile Elite = new()
    {
        AimError = 0f, ShootDelay = 0f, ReacquireDelay = 0f, GunneryError = 0f, Leading = 1f, Handling = 1.1f,
        Evasion = 1.25f, DodgesFire = true, DamageTaken = 1f, DamageDealt = 0.86f,
        Braces = true, Throws = true, CookTime = 1.3f, Sidesteps = true, FallsBack = true, HuntParty = 2,
    };

    public static Profile Of(WGCrewSkill skill)
    {
        return skill switch
        {
            WGCrewSkill.Green => Green,
            WGCrewSkill.Regular => Regular,
            WGCrewSkill.Elite => Elite,
            _ => Veteran,
        };
    }

    /// <summary>The crewman's own level: his crew's skill moved by his <see cref="WGCrewComponent.SkillOffset"/>.</summary>
    public static WGCrewSkill Effective(WGCrewComponent crew)
    {
        var level = Math.Clamp((int) crew.Skill + crew.SkillOffset, (int) WGCrewSkill.Green, (int) WGCrewSkill.Elite);
        return (WGCrewSkill) level;
    }

    /// <summary>The profile of the crewman's own level.</summary>
    public static Profile Of(WGCrewComponent crew) => Of(Effective(crew));
}
