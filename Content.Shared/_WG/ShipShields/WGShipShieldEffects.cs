namespace Content.Shared._WG.ShipShields;

/// <summary>Pure sampling rules for shield health, cooling and impact waves.</summary>
public static class WGShipShieldEffects
{
    /// <summary>Duration of residual local impact heat.</summary>
    public const float HeatLifetime = 16f;
    /// <summary>Duration of an expanding impact ripple.</summary>
    public const float WaveLifetime = 4f;
    /// <summary>Ripple travel speed in tiles per second.</summary>
    public const float WaveSpeed = 22f;

    /// <summary>Share of its heat capacity at which a ship stops firing through its raised field.</summary>
    public const float GunHoldHeat = 0.95f;
    /// <summary>Share of its heat capacity a held ship must cool below before its guns fire again.</summary>
    public const float GunResumeHeat = 0.85f;
    /// <summary>Seconds after the field's last hit for which hard heat stays under a raised field.</summary>
    public const float HardHeatHold = 30f;
    /// <summary>How many times its plain rate a ship cools with no field up, a bare hull included.</summary>
    public const float FieldDownCooling = 2f;
    /// <summary>How many times its plain rate a venting ship cools.</summary>
    public const float VentCooling = 6f;
    /// <summary>Heat each point of damage a ship generator's field absorbs adds, unless the generator sets its own.</summary>
    public const float HitHeat = 0.8f;
    /// <summary>Most of a ship's heat capacity one hit absorbed by its field can add.</summary>
    public const float HitHeatCap = 0.2f;
    /// <summary>Ship heat capacity at which overload waits and the shortest full vent are as configured.</summary>
    public const float ReferenceCapacity = 40000f;
    /// <summary>Least seconds a ship at <see cref="ReferenceCapacity"/> takes to vent a full load.</summary>
    public const float FullVentSeconds = 12f;
    /// <summary>Least seconds any vent lasts, however little heat it sheds.</summary>
    public const float MinVentSeconds = 3f;
    /// <summary>Damage of a hit that charges a ship's field in full; a smaller hit charges its share of this.</summary>
    public const float FullHitDamage = 200f;
    /// <summary>Least share of its damage a small hit charges a ship's field.</summary>
    public const float MinHitWeight = 0.25f;

    /// <summary>
    /// How much longer than configured a ship of <paramref name="capacity"/> waits out an overload and vents a full
    /// load: the square root of its size against <see cref="ReferenceCapacity"/>, from 0.75 to 1.5.
    /// </summary>
    public static float SizeScale(float capacity)
    {
        if (!float.IsFinite(capacity) || capacity <= 0f)
            return 1f;
        return Math.Clamp(MathF.Sqrt(capacity / ReferenceCapacity), 0.75f, 1.5f);
    }

    /// <summary>Seconds a ship of <paramref name="capacity"/> waits out an overload: its generator's configured wait scaled by its size.</summary>
    public static float OverloadWait(float configured, float capacity) => configured * SizeScale(capacity);

    /// <summary>
    /// Heat per second a venting ship sheds: <see cref="VentCooling"/> times its plain rate, but never so fast that a
    /// full load goes in under <see cref="FullVentSeconds"/> scaled by its size.
    /// </summary>
    public static float VentRate(float plainRate, float capacity)
    {
        var rate = MathF.Max(0f, plainRate) * VentCooling;
        if (!float.IsFinite(capacity) || capacity <= 0f)
            return rate;
        return MathF.Min(rate, capacity / (FullVentSeconds * SizeScale(capacity)));
    }

    /// <summary>Share of a hit's damage a ship's field is charged: in full from <see cref="FullHitDamage"/>, less for small rounds.</summary>
    public static float HitWeight(float hit)
    {
        if (!float.IsFinite(hit) || hit >= FullHitDamage)
            return 1f;
        return Math.Clamp(hit / FullHitDamage, MinHitWeight, 1f);
    }

    /// <summary>
    /// Heat a hull holds by its tiles: each adds <paramref name="perTile"/> at first, tapering toward
    /// <paramref name="max"/> so a huge hull is not a huge shield.
    /// </summary>
    public static float HullCapacity(float baseCapacity, float perTile, float max, int tiles)
    {
        var span = max - baseCapacity;
        if (!float.IsFinite(span) || span <= 0f)
            return MathF.Max(0f, MathF.Min(baseCapacity, max));
        return baseCapacity + span * (1f - MathF.Exp(-Math.Max(0, tiles) * MathF.Max(0f, perTile) / span));
    }

    /// <summary>
    /// What a shell's blast counts as for the heat of the ship that fires it: the damage of its peak tile, not the sum
    /// over every tile it spreads to.
    /// </summary>
    public static float BlastDamage(float totalIntensity, float maxIntensity, float damagePerIntensity)
    {
        return MathF.Max(0f, MathF.Min(maxIntensity, totalIntensity) * damagePerIntensity);
    }

    /// <summary>What a shell's blast charges a field that stops or slows it: the whole blast, every tile it would reach.</summary>
    public static float AbsorbedBlastDamage(float totalIntensity, float damagePerIntensity)
    {
        return MathF.Max(0f, totalIntensity * damagePerIntensity);
    }

    /// <summary>
    /// A value for display, at most two decimals. Rounded as a double: a rounded float still prints its binary tail
    /// (1417.9000244140625) once Fluent widens it.
    /// </summary>
    public static double Display(float value) => Math.Round((double) value, 2);

    /// <summary>
    /// Heat per second a ship of <paramref name="capacity"/> sheds in its state from its plain rate; every ship, with or
    /// without a generator, uses this. Raised or overloaded it cools at the plain rate (an overload as in Starsector,
    /// so the wait is no free refill), otherwise down at <see cref="FieldDownCooling"/> times it, and venting at
    /// <see cref="VentRate"/>.
    /// </summary>
    public static float CoolingRate(float plainRate, float capacity, bool raised, bool venting, bool overloaded)
    {
        if (venting)
            return VentRate(plainRate, capacity);
        return MathF.Max(0f, plainRate) * (raised || overloaded ? 1f : FieldDownCooling);
    }

    /// <summary>Returns full capacity bounded by overload and power demand limits.</summary>
    public static float EffectiveCapacity(float damageLimit, float maxDraw, float powerModifier, float damageExp)
    {
        if (!float.IsFinite(damageLimit) || damageLimit <= 0f)
            return 0f;
        if (!float.IsFinite(maxDraw) || maxDraw <= 0f || !float.IsFinite(powerModifier) || powerModifier <= 0f ||
            !float.IsFinite(damageExp) || damageExp <= 0f)
            return damageLimit;
        return (float) Math.Min(damageLimit, Math.Pow((double) maxDraw / powerModifier, 1d / damageExp));
    }

    /// <summary>Scales impact energy against full capacity, reaching full intensity at five percent.</summary>
    public static float ImpactStrength(float damage, float capacity)
    {
        if (!float.IsFinite(damage) || damage <= 0f || !float.IsFinite(capacity) || capacity <= 0f)
            return 0f;
        return Math.Clamp((float) Math.Sqrt((double) damage / capacity / 0.05d), 0.08f, 1f);
    }

    /// <summary>Scales impact footprint and ripple reach with impact energy.</summary>
    public static float ImpactRadiusScale(float strength) =>
        0.2f + 0.8f * Math.Clamp(float.IsFinite(strength) ? strength : 0f, 0f, 1f);

    /// <summary>Returns the default health indicator colour.</summary>
    public static Color HealthColor(float health) => HealthColor(health, new Color(0.12f, 0.72f, 1f));

    /// <summary>Blends the generator colour through amber to red as shield health falls.</summary>
    public static Color HealthColor(float health, Color generatorColor)
    {
        health = float.IsFinite(health) ? Math.Clamp(health, 0f, 1f) : 0f;
        return health > 0.45f
            ? Color.InterpolateBetween(new Color(1f, 0.53f, 0.12f), generatorColor.WithAlpha(1f), (health - 0.45f) / 0.55f)
            : Color.InterpolateBetween(new Color(1f, 0.05f, 0.08f), new Color(1f, 0.53f, 0.12f), health / 0.45f);
    }

    /// <summary>Returns local impact heat with seven-second cooling and a sixteen-second lifetime.</summary>
    public static float Heat(float distance, float age, float strength)
    {
        if (age < 0f || age >= HeatLifetime)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        return strength * 0.65f * MathF.Exp(-age / 7f) * MathF.Exp(-distance * distance / 14f) *
            Math.Clamp((HeatLifetime - age) / 4f, 0f, 1f);
    }

    /// <summary>Briefly reveals nearby hexes before the traveling ripple leaves the impact.</summary>
    public static float Flash(float distance, float age, float strength)
    {
        if (age < 0f || age >= 0.65f)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        var fade = 1f - age / 0.65f;
        return strength * fade * fade * MathF.Exp(-distance * distance / 10f);
    }

    /// <summary>Layers local red heat and impact flashes over the generator's health colour.</summary>
    public static (Color Tint, float Surface, float Hexes, Color SurfaceTint) Appearance(Color health, float heat, float wave, float flash, float integrity = 1f, float wake = 0f)
    {
        heat = Math.Clamp(heat, 0f, 1.5f);
        wave = Math.Clamp(wave, 0f, 1.5f);
        flash = Math.Clamp(flash, 0f, 1f);
        wake = Math.Clamp(wake, 0f, 1.5f);
        var damage = 1f - Math.Clamp(float.IsFinite(integrity) ? integrity : 0f, 0f, 1f);
        var hot = Math.Clamp(heat * 1.5f, 0f, 1f);
        var red = new Color(1f, 0.025f, 0.045f);
        var tint = Color.InterpolateBetween(health, red, Math.Clamp(hot + wake * 0.85f, 0f, 1f));
        var surfaceTint = tint;
        tint = Color.InterpolateBetween(tint, Color.White, flash);
        surfaceTint = Color.InterpolateBetween(surfaceTint, Color.White, flash);
        return (Color.FromSrgb(tint),
            0.06f + damage * 0.38f + flash * 1.25f + wave * 1.15f + heat * 0.55f + wake * 0.55f,
            0.38f + damage * 0.62f + flash * 3.2f + wave * 2.8f + heat * 1.2f + wake * 1.4f,
            Color.FromSrgb(surfaceTint));
    }

    /// <summary>Returns a broad brightness crest traveling up to eighty-eight tiles from the impact.</summary>
    public static float Wave(float distance, float age, float strength)
    {
        if (age < 0f || age >= WaveLifetime)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        var ring = distance - age * WaveSpeed;
        return strength * MathF.Sqrt(1f - age / WaveLifetime) * MathF.Exp(-ring * ring / 2.4f);
    }

    /// <summary>Returns the wider red wake following the brightness crest.</summary>
    public static float WaveWake(float distance, float age, float strength)
    {
        if (age < 0f || age >= WaveLifetime)
            return 0f;
        distance /= ImpactRadiusScale(strength);
        var trailing = distance - MathF.Max(0f, age * WaveSpeed - 3f);
        return strength * 0.75f * MathF.Sqrt(1f - age / WaveLifetime) *
            MathF.Exp(-trailing * trailing / 9f);
    }
}