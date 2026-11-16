using Robust.Shared.Configuration;

namespace Content.Shared._WF.InteractionParticles;

/// <summary>
/// Client-side interaction particle visibility.
/// </summary>
public enum InteractionParticleMode
{
    /// <summary>
    /// Shows ordinary interaction particles and the local player's in-hand particles.
    /// </summary>
    All = 0,

    /// <summary>
    /// Shows ordinary interaction particles but no in-hand/inventory particles.
    /// </summary>
    WithoutInHand = 1,

    /// <summary>
    /// Hides every interaction particle.
    /// </summary>
    None = 2,
}

/// <summary>
/// Settings for interaction particles.
/// </summary>
[CVarDefs]
public sealed class InteractionParticleCVars
{
    /// <summary>
    /// Controls which interaction particles are displayed.
    /// </summary>
    public static readonly CVarDef<int> Mode =
        CVarDef.Create("opt.interaction_particles_mode", (int) InteractionParticleMode.WithoutInHand, CVar.CLIENTONLY | CVar.ARCHIVE);
}
