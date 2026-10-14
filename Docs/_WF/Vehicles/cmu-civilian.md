# CMU civilian vehicles and emergency lights

The civilian half of the CMU package: 17 vehicles, their five interiors and the emergency lights. The military half (the
tanks, the APC, the AEV and the CMU hardpoints) is in `cmu-military.md`. Ported from Colonial Marines Universe (`cmu` in
`port-manifest.md`, AGPL-3.0); CMU's own C# is not copied except the emergency lights.

## The vehicles

`Prototypes/_WF/Vehicles/CMU/civilian_vehicles.yml`. Every vehicle keeps its CMU id (typos included: `AU14VehicleCivturquiseCargo`),
name and description, and is a child of `VehicleVanBase` or `VehicleBoxVanBase`, so it has the van family's handling,
integrity, wheel, horn and overcharge. A child lists its sprite, interior, doorways, seats and price and nothing else.

- On the van base: the civilian van, truck and armored truck (the last two share the `truck_base` and `armored_base` states of
  CMU's van sprite sheet).
- On the box van base: the Hyperdyne van, ten small trucks and vans (`civtruck_1` to `_3`, turquoise cargo, medical, garbage,
  brown barrels, brown cargo, blue cargo, blue), the ambulance, the police car and the CMB paddywagon.
- The ambulance, police car and paddywagon carry `CMUEmergencyLights`.
- The CMB paddywagon keeps the box van's interior, as CMU does: CMU's own paddywagon map is marked broken there and is not
  converted.

CMU's numbers for speed, wall smashing, crash lock-out and the rest of its mover are not used (the family's are); the van
sprites are 96 by 96 on the box van's fixture, as in CMU, so art and hitbox do not match on the small trucks.

### Doorways

CMU's trucks had broken doorway lists: the two on the van base had one at the vehicle's centre (radius 0.1) and the same one
twice, and the seven small trucks on the box van base had a second left-side door where the back door goes. Each has a right,
a left and a back door like its base. The trucks' second door lands on the passenger seat of the cab, next to the door marker it
leaves by (the universal roster test clicks that marker from the landing spot). The police car has two doorways: its interior
has exit markers for two.

CMU landed every doorway of the Hyperdyne van, the ambulance and the police car at -1.3,1, mid-cabin, which is 2.3 tiles from
the front doors' markers. Each doorway lands beside its own marker with the box van's spots (-0.42,1.6 and -0.53,0.84 for
the side doors), and the front doors at -2.6,0.5, against the west wall and clear of the ambulance's operating table. The
universal roster test checks that every landing is within 2.5 tiles of the nearest exit marker with its entry index.

### Prices

`StaticPrice` is a placeholder on every vehicle, for the owner to set: 8000 for the garbage truck, 10000 for the other small
trucks, 12000 for the civilian van and truck and the Hyperdyne van, 18000 for the ambulance, police car and paddywagon, 20000
for the armored truck. The van base's own is 15000.

## Interiors

`Maps/_WF/Vehicles/Interiors/cmu_*.yml`, converted by `convert_interior_map.py` from `Ambulance`, `CopMonoSupron`,
`PaddyWagonVanEmpty`, `truck_interior` and `van_interior` (CMU's `arv` belongs to the AEV). They are built of the RMC van and box
van pieces already in the module. What CMU placed that Wolfgate has a twin for: `ComfyChair`, `OperatingTable` for the
portable surgical bed, `WFWolfmedIvDrip`, `DefibrillatorCabinetFilled`, `RandomPosterAny`, `AlwaysPoweredlightBlue`. The radio
sets, the stasis bag, the loot spawner and the stray grab effect are deleted. The police car's cell doors are
`WFVehicleWindoorSecure` (`CMU/civilian_interiors.yml`): a secure windoor that needs no power, for the same reason as
`WFVehicleWindoor`.

## Emergency lights

`CMUEmergencyLights` on a vehicle gives its operator the `CMUActionVehicleEmergencyLights` action, which steps the vehicle's
lightbar through off, slow, medium and fast (`CMUEmergencyLightbarSlow`, `Medium`, `Fast`, a red and blue `ColorCycleBehaviour`
light spawned on the vehicle). The lights stay on when the operator leaves and the next operator's action shows them on; the
lightbar goes with the vehicle.

It is CMU's `CMUEmergencyLightsSystem` on this module's operator: the vehicle's `VehicleOperatorSetEvent` grants the action and
the action event is handled on `RMCVehicleOperator`, only for the vehicle's current operator. The action is Wolfgate's
single-component `InstantAction` (not temporary: the system deletes the action entity itself), its icon is Wolfgate's
`emergency_light.rsi`, and the popup is the `cmu-emergency-lights-mode` line of `cmu-civilian.ftl`.

## Tests

`VehicleRosterTest` covers all 17 (they are 17 of its 50). `CMUEmergencyLightsTest` goes over every prototype that carries
the component: the operator gets the action and nobody else, using it from the client cycles the lights and the action's toggled
state, a bystander and a driver who stepped out cannot use it, the action is gone when the operator leaves, and the lightbar
goes with the vehicle.
