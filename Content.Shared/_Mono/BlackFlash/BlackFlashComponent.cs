using Content.Shared.Damage; // WOLFGATE: resolve the Black Flash damage specification type
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.BlackFlash;

// WOLFGATE: document the armed action's flat damage bonus
/// <summary>
/// Grants the Black Flash action. Arm it, land a melee hit inside the window, and the target takes
/// a flat damage bonus and gets launched.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BlackFlashComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool EmptyHandedOnly = false;

    [DataField]
    public EntProtoId Action = "ActionBlackFlash";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField, AutoNetworkedField]
    public TimeSpan HitCooldown = TimeSpan.FromMinutes(1);

    [DataField, AutoNetworkedField]
    public TimeSpan MissCooldown = TimeSpan.FromSeconds(10);

    // WOLFGATE START: flat bonus added after melee modifiers, about 100 with a bare fist
    /// <summary>
    /// Damage the armed hit adds after melee modifier sets; armor still applies.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier Damage = new()
    {
        DamageDict = new()
        {
            { "Blunt", 95 },
        }
    };
    // WOLFGATE END

    [DataField, AutoNetworkedField]
    public float StaminaCost = 30f;

    [DataField, AutoNetworkedField]
    public TimeSpan Hitstop = TimeSpan.FromSeconds(0.2);

    [DataField, AutoNetworkedField]
    public TimeSpan StunTime = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField]
    public float ThrowDistance = 15f;

    [DataField, AutoNetworkedField]
    public float ThrowSpeed = 25f;

    [DataField, AutoNetworkedField]
    public EntProtoId HitEffect = "EffectBlackFlash";

    [DataField, AutoNetworkedField]
    public EntProtoId MissEffect = "EffectBlackFlashWhiff";

    [DataField, AutoNetworkedField]
    public SoundSpecifier? HitSound = new SoundPathSpecifier("/Audio/_Mono/Items/black_flash.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? MissSound = new SoundPathSpecifier("/Audio/_Mono/Items/black_flash_fumble.ogg");
}
