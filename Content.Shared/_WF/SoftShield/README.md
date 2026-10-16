# SoftShield

Soft shields for the PDV CV-53 and TSFMC M92-X tacsuits. Where a hard personal shield blocks a hit outright, a soft
shield lets it through: it soaks a fixed share (60%) of brute damage (Blunt, Slash, Piercing) from its charge, and
burns and every other type reach the suit's armour untouched. While the field is up the wearer opens no new bleed and
no hit deepens an old one. The field toggles like any personal shield and still fractures when its charge runs out or a
shield-breaker round hits it.

Entry points: `WFSoftShieldComponent` on the suit (`damageGroup`, `reduction`) and `WFSoftShieldSystem`, which dampens
at the armour step (`PartDamageModifyEvent` for a wound host, `DamageModifyEvent` otherwise) so each hit is soaked
once, and cancels Wolfmed's `WolfmedBleedAttemptEvent`. `SharedPersonalShieldSystem` skips soft shields in its
hard-block handler. `WFSoftShieldTest` covers both suits.

<!-- WOLFGATE-GENERATED START -->
<!-- WOLFGATE-GENERATED END -->
