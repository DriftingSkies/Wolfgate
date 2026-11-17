namespace Content.Server._WG.Remade.Abilities;

/// <summary>Springing out of hiding: a leap or pounce at close range with almost no crouch.</summary>
public sealed partial class WGAbilitySystem
{
    /// <summary>Seconds of crouch an ambusher takes before it leaps, against a second or more in a fair fight.</summary>
    private const float AmbushWindUp = 0.25f;

    /// <summary>
    /// Starts the mob's leap or pounce at a target from wherever it is, however close, with a short wind-up. The landing hurts and
    /// knocks the target down as usual. False if it has neither or can't use it.
    /// </summary>
    public bool Ambush(EntityUid remade, EntityUid target)
    {
        if (!TryComp<WGRemadeAbilitiesComponent>(remade, out var comp) || comp.Active != null || !IsLive(target))
            return false;

        var ent = new Entity<WGRemadeAbilitiesComponent>(remade, comp);
        var ctx = new WGAbilityContext(this, remade, target, true, _timing.CurTime);
        foreach (var ability in comp.Abilities)
        {
            if (ability is not (WGLeapAbility or WGPounceAbility) || !Allowed(ent, ability))
                continue;

            var min = ability.MinRange;
            ability.MinRange = 0f;
            var result = WGAbilityResult.None;
            switch (ability)
            {
                case WGLeapAbility leap:
                {
                    var wind = leap.WindUp;
                    leap.WindUp = AmbushWindUp;
                    result = leap.TryStart(ctx);
                    leap.WindUp = wind;
                    break;
                }
                case WGPounceAbility pounce:
                {
                    var wind = pounce.WindUp;
                    pounce.WindUp = AmbushWindUp;
                    result = pounce.TryStart(ctx);
                    pounce.WindUp = wind;
                    break;
                }
            }

            ability.MinRange = min;
            if (Begin(ent, ability, result, ctx))
                return true;
        }

        return false;
    }
}
