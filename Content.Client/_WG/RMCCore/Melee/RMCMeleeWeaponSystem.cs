using Content.Shared._WG.RMCCore.Melee;

namespace Content.Client._WG.RMCCore.Melee;

// WOLFGATE: RMC's xeno wide-swing keybind needs MeleeWeaponComponent.WidePrimary and a public ClientHeavyAttack, both
// RMC edits to upstream melee; the Xenonids module decides whether to bring them.
public sealed class RMCMeleeWeaponSystem : SharedRMCMeleeWeaponSystem;
