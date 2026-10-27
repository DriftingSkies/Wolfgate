# ClothingLayer

SS13's rules for how worn clothing stacks, for all clothing, native and ported: which piece draws over which, and
which pieces hide others. `ClientClothingSystem` asks `ClothingLayerSystem` where to draw each slot and whether to
draw it at all. Most clothing needs nothing: the rules below read its art. The two components are for what the art
can't show.

## The draw order

A humanoid is drawn bottom to top in this order (`Resources/Prototypes/Entities/Mobs/Species/base.yml`; other species
are close to it). Each slot's clothing draws at that slot's place.

1. body: tail behind, chest, head, snout, eyes, arms, legs, underwear
2. `jumpsuit`, then the feet and hands, then `gloves`, `shoes`
3. `ears`, `id`, `outerClothing`, `eyes`, `belt`, `back`
4. facial hair, hair, head markings (ears, frills), wings
5. `balaclava`, `mask`, `head`, `neck`, `neck2`, `neck3`
6. tail, `helmetcover`, `helmetattachment`, `pocket1`, `pocket2`, handcuffs

## Rules from the art

These apply to every item, native or ported, with nothing to set:

- **Uniforms draw over the feet** and under the hands, as in SS13. SS14 draws the bare feet over the jumpsuit, which
  shows them through long dresses. This applies on bodies whose feet sit between the uniform and the shoes; a species
  that draws its feet higher on purpose keeps them there.
- **Draping uniforms draw over the shoes**: those with fabric at shoe height that isn't on the legs, such as skirts,
  gowns, robes and baggy trousers. Fitted trousers, tights and short skirts stay under the shoes.
- **Enclosing outer suits hide the uniform**: those covering the whole body (torso, arms and legs) from all four sides,
  such as hardsuits and EVA, bomb, fire and bio suits. A dress can't poke out of one. Worn open (any equipped prefix),
  a suit hides nothing.

`Tools/_WF/ClothingLayer/shapes.py` measures which sprites drape or enclose against the human body sprite. It writes
the result to `Resources/Prototypes/_WF/ClothingLayer/shapes.yml` (`ClothingShapesPrototype`, keyed by RSI path),
which is never edited by hand. Run it after adding or redrawing a uniform or outer suit:

```sh
python Tools/_WF/ClothingLayer/shapes.py --write
```

CI runs `--check` and fails while the list is stale.

### When the measurement is wrong

If a sprite is measured wrong (a skirt that should cover boots but is drawn under them, or a suit that shows skin when
it hides the uniform), correct it in `Tools/_WF/ClothingLayer/overrides.yml` and run `--write`. Entries are RSI paths
with a reason:

```yaml
drapes:
  add:
    Clothing/Uniforms/Jumpskirt/example.rsi: hem is drawn just above the ankle and knee boots cover it
  remove: {}
encloses:
  add: {}
  remove:
    Clothing/OuterClothing/Suits/example.rsi: open at the back, the north sprite shows skin without the uniform
```

An entry for a sprite that no longer exists fails the run.

## Rules an item sets itself

For what the art can't show, such as a mask meant to sit under the hair, add a component to the item's prototype.

### `WFClothingLayer`: draw somewhere else in the order

Draws the item beside another layer of the wearer instead of at its slot's place. `layer` is a slot name from the
list above, or a body layer as an enum reference (`enum.HumanoidVisualLayers.Hair`, `.FacialHair`, `.RFoot`, ...). The
item draws just above that layer and everything that slot is wearing, or with `below: true` just under it.

```yaml
# A balaclava under the glasses and headset: drawn just below the ears slot.
- type: WFClothingLayer
  layer: ears
  below: true

# Welding goggles over a hat.
- type: WFClothingLayer
  layer: head
```

It is SS13's `alternate_worn_layer`. A layer the wearer doesn't have (a mannequin has no feet) leaves the item at its
slot's place. This takes precedence over the draping rule.

### `WFHideClothingSlots`: hide other slots

While worn, the clothing in the listed slots isn't drawn. Slot names are the inventory slots: `jumpsuit`, `shoes`,
`gloves`, `mask`, `balaclava`, `eyes`, `ears`, `head`, `neck`, `neck2`, `neck3`, `belt`, `back`, `suitstorage`, `id`,
`helmetcover`, `helmetattachment`.

```yaml
# A full helmet hides the mask, glasses and headset under it.
- type: WFHideClothingSlots
  slots: [mask, balaclava, ears, eyes]

# A robe to the floor hides the shoes.
- type: WFHideClothingSlots
  slots: [shoes]
```

It is SS13's `flags_inv`. An item hides only while worn in a slot it's made for (not in a pocket) and as made: with
any equipped prefix, such as a mask pulled down or a coat worn open, it hides nothing. Only drawing changes; the
hidden items stay equipped and keep working.

## Ported clothing

The MeridianClothing port (`Tools/_WF/MeridianClothing/port.py`) reads SS13's own settings and adds both components:
`WORN_LAYERS` maps `alternate_worn_layer` to `WFClothingLayer`, and `SLOT_HIDES` maps `flags_inv` to
`WFHideClothingSlots`. Change those tables, not the generated prototypes.

## How it works

`ClientClothingSystem` calls two methods for each slot it draws (two marked hooks):

- `ClothingLayerSystem.TryGetIndex` picks where the slot's layers go. In order of precedence: the item's
  `WFClothingLayer`, the draping rule, the feet rule, and otherwise the slot's own place.
- `ClothingLayerSystem.IsHidden` says whether anything else worn covers the slot.

When something that can hide is put on, taken off, opened or pulled down, the system redraws only the slots it
covers.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Shared

- [`Content.Shared/_WF/ClothingLayer/ClothingShapesPrototype.cs`](ClothingShapesPrototype.cs)
- [`Content.Shared/_WF/ClothingLayer/WFClothingLayerComponent.cs`](WFClothingLayerComponent.cs)
- [`Content.Shared/_WF/ClothingLayer/WFHideClothingSlotsComponent.cs`](WFHideClothingSlotsComponent.cs)

### Client

- [`Content.Client/_WF/ClothingLayer/ClothingLayerSystem.cs`](../../../Content.Client/_WF/ClothingLayer/ClothingLayerSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/ClothingLayer/ClothingLayerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ClothingLayer/ClothingLayerTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/ClothingLayer/shapes.yml`](../../../Resources/Prototypes/_WF/ClothingLayer/shapes.yml)

### Tools

- [`Tools/_WF/ClothingLayer/overrides.yml`](../../../Tools/_WF/ClothingLayer/overrides.yml)
- [`Tools/_WF/ClothingLayer/shapes.py`](../../../Tools/_WF/ClothingLayer/shapes.py)

## Non-modular edits

- [`.github/workflows/wf-modules.yml`](../../../.github/workflows/wf-modules.yml)
  - shapes.py reads the clothing sprites
  - fails when a clothing sprite was added or changed without regenerating the shapes list
- [`Content.Client/Clothing/ClientClothingSystem.cs`](../../../Content.Client/Clothing/ClientClothingSystem.cs)
  - clothing covered by other clothing isn't drawn, like SS13's flags_inv
  - an item may draw at another layer's place, like SS13's alternate_worn_layer

<!-- WOLFGATE-GENERATED END -->
