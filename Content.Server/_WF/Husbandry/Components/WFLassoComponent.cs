using Robust.Shared.Utility;

namespace Content.Server._WF.Husbandry.Components;

/// <summary>A rope thrown over an animal to lead it: a few seconds' struggle a loop, and two loops for a big one.</summary>
[RegisterComponent, Access(typeof(WFHusbandrySystem))]
public sealed partial class WFLassoComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3);

    /// <summary>How far the lasso is thrown.</summary>
    [DataField]
    public float Range = 4f;

    /// <summary>How much rope the animal is led on.</summary>
    [DataField]
    public float Length = 3f;

    /// <summary>Animals at least this heavy, in kilograms, take two loops.</summary>
    [DataField]
    public float BigMass = 50f;

    [DataField]
    public SpriteSpecifier RopeSprite = new SpriteSpecifier.Rsi(new ResPath("Objects/Weapons/Guns/Launchers/grappling_gun.rsi"), "rope");

    /// <summary>The animal on the lasso's rope.</summary>
    [ViewVariables]
    public EntityUid? Roped;

    /// <summary>A big animal with a first loop over it, and how many loops it has.</summary>
    [ViewVariables]
    public EntityUid? Looped;

    [ViewVariables]
    public int Loops;
}

/// <summary>An animal on a lasso's rope, held by whoever threw it.</summary>
[RegisterComponent, Access(typeof(WFHusbandrySystem))]
public sealed partial class WFLassoedComponent : Component
{
    [ViewVariables]
    public EntityUid Holder;

    [ViewVariables]
    public EntityUid Lasso;
}
