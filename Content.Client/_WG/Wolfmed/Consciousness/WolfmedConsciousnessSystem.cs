using Content.Shared._WG.Wolfmed.Consciousness;

namespace Content.Client._WG.Wolfmed.Consciousness;

/// <summary>
/// Client half of consciousness. The evaluation is server-only (pain, blood and airloss all are), so this
/// exists so the shared MobThresholds gate can resolve the system on both sides.
/// </summary>
public sealed class WolfmedConsciousnessSystem : SharedWolfmedConsciousnessSystem;
