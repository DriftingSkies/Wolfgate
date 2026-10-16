# ArmorBalance

Wolfgate's sidegrade pass over the TSF and PDV outer suits. Each tier has a protection budget, the sum of Blunt,
Slash, Piercing and Heat damage taken: 2.5 at T2 and 2.1 at T3. Within a tier every suit spends that budget on a
different role, and pays extra budget for speed (about 0.2 per +0.05 above the tier's standard) or a soft shield
(about 0.4 for 100 charge, 0.5 for 200). No single type goes below 0.4 or above 0.8.

| Tier | Suit | Role |
|---|---|---|
| T2 | M82c | TSF generalist |
| T2 | M82b | Burn and environment |
| T2 | SCAF | PDV burn |
| T2 | CV-32 | Anti-ballistic |
| T2 | CV-280 | Mobility |
| T2 | CV-53 | Soft shield (SoftShield module) |
| T3 | M86 Mk.3 | Slow, tough generalist |
| T3 | M86 Mk.4 | Fast heavy; its hard shield is removed |
| T3 | M92-X | Soft-shield juggernaut |
| T3 | CV-82 | Fast medic, left as it was |
| Command | M92 colonel | Toughest suit, by a small margin |

PDV suits cannot be crafted (only the SCAF has a researchable recipe), so the CV-32, CV-53 and CV-82 cost 10
doubloons less at the pirate uplink. Values live on the suit prototypes in `_Mono` with `WOLFGATE(ArmorBalance)`
markers.

<!-- WOLFGATE-GENERATED START -->
<!-- WOLFGATE-GENERATED END -->
