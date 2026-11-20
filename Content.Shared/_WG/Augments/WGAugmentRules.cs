using System.Linq;
using System.Text;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared._WG.Biotype;
using Content.Shared._WG.LegStyle;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Augments;

/// <summary>Where each slot sits on a body, which augments and styles a character may pick, and how they are stored.</summary>
public static class WGAugmentRules
{
    private const char EntrySeparator = ';';
    private const char FieldSeparator = ':';
    private const string ImplantEntry = "Implant";
    private const string FoundEntry = "Found";
    private const string FoundSuffix = ":" + FoundEntry;

    /// <summary>Slots implants were stored under before a part could hold several.</summary>
    private static readonly string[] LegacyImplantSlots = ["LeftArmImplant", "RightArmImplant", "Dermal", "Muscles"];

    /// <summary>The body slot holding the part a limb augment replaces, or the organ an organ augment replaces.</summary>
    public static string PartSlot(WGAugmentSlot slot) => slot switch
    {
        WGAugmentSlot.Head or WGAugmentSlot.Eyes => "head",
        WGAugmentSlot.LeftArm => "left arm",
        WGAugmentSlot.RightArm => "right arm",
        WGAugmentSlot.LeftLeg => "left leg",
        WGAugmentSlot.RightLeg => "right leg",
        WGAugmentSlot.LeftHand => "left hand",
        WGAugmentSlot.RightHand => "right hand",
        WGAugmentSlot.LeftFoot => "left foot",
        WGAugmentSlot.RightFoot => "right foot",
        _ => "torso",
    };

    /// <summary>The share of its price, in percent, an augment taken off in the creator pays back.</summary>
    public const int RefundPercent = 50;

    /// <summary>What a paid-for augment taken off in the creator pays back.</summary>
    public static int Refund(WGAugmentPrototype augment) => augment.Price * RefundPercent / 100;

    /// <summary>What an augment the profile owns pays back when taken off: nothing for one kept from a round.</summary>
    public static int Refund(HumanoidCharacterProfile profile, WGAugmentPrototype augment)
    {
        return profile.AugmentFound.Contains(augment.ID) ? 0 : Refund(augment);
    }

    /// <summary>Implants a part the species was born with holds: two in the torso, one in any other part.</summary>
    public static int NaturalImplantSlots(WGAugmentSlot slot) => slot == WGAugmentSlot.Chest ? 2 : 1;

    /// <summary>The limb a hand or foot sits on, which it comes with unless picked on its own.</summary>
    public static WGAugmentSlot? LimbAbove(WGAugmentSlot slot) => slot switch
    {
        WGAugmentSlot.LeftHand => WGAugmentSlot.LeftArm,
        WGAugmentSlot.RightHand => WGAugmentSlot.RightArm,
        WGAugmentSlot.LeftFoot => WGAugmentSlot.LeftLeg,
        WGAugmentSlot.RightFoot => WGAugmentSlot.RightLeg,
        _ => null,
    };

    /// <summary>The entity an augment puts in the body, which is also what scavengers find of it.</summary>
    public static EntProtoId? Installed(WGAugmentPrototype augment) => augment.Kind switch
    {
        WGAugmentKind.Limb => augment.Part,
        WGAugmentKind.Organ => augment.Organ,
        WGAugmentKind.Upgrade => augment.Autosurgeon,
        _ => null,
    };

    /// <summary>The augment's name: its own, else the installed entity's.</summary>
    public static string Name(WGAugmentPrototype augment, IPrototypeManager proto)
    {
        if (augment.Name is { } name)
            return Loc.GetString(name);
        return Installed(augment) is { } id && proto.TryIndex(id, out var entity) ? entity.Name : augment.ID;
    }

    /// <summary>The augment's description: its own, else the installed entity's.</summary>
    public static string Description(WGAugmentPrototype augment, IPrototypeManager proto)
    {
        if (augment.Description is { } description)
            return Loc.GetString(description);
        return Installed(augment) is { } id && proto.TryIndex(id, out var entity) ? entity.Description : string.Empty;
    }

    /// <summary>How often scavengers find an augment, against the others: the inverse of its price.</summary>
    public static float ScavengeWeight(WGAugmentPrototype augment)
    {
        return augment.Price > 0 && Installed(augment) != null ? 100_000f / augment.Price : 0f;
    }

    /// <summary>The organ slot an organ augment fills, or null for a part.</summary>
    public static string? OrganSlot(WGAugmentSlot slot) => slot switch
    {
        WGAugmentSlot.Eyes => "eyes",
        WGAugmentSlot.Heart => "heart",
        WGAugmentSlot.Lungs => "lungs",
        WGAugmentSlot.Liver => "liver",
        WGAugmentSlot.Stomach => "stomach",
        _ => null,
    };

    /// <summary>The humanoid layers a limb or plating in this slot draws over.</summary>
    public static HumanoidVisualLayers[] Layers(WGAugmentSlot slot) => slot switch
    {
        WGAugmentSlot.Head => [HumanoidVisualLayers.Head],
        WGAugmentSlot.Chest => [HumanoidVisualLayers.Chest],
        WGAugmentSlot.LeftArm => [HumanoidVisualLayers.LArm, HumanoidVisualLayers.LHand],
        WGAugmentSlot.RightArm => [HumanoidVisualLayers.RArm, HumanoidVisualLayers.RHand],
        WGAugmentSlot.LeftLeg => [HumanoidVisualLayers.LLeg, HumanoidVisualLayers.LFoot],
        WGAugmentSlot.RightLeg => [HumanoidVisualLayers.RLeg, HumanoidVisualLayers.RFoot],
        WGAugmentSlot.LeftHand => [HumanoidVisualLayers.LHand],
        WGAugmentSlot.RightHand => [HumanoidVisualLayers.RHand],
        WGAugmentSlot.LeftFoot => [HumanoidVisualLayers.LFoot],
        WGAugmentSlot.RightFoot => [HumanoidVisualLayers.RFoot],
        _ => [],
    };

    /// <summary>Whether the character's body has the part or organ this slot works on.</summary>
    public static bool SlotAvailable(string species,
        WGAugmentSlot slot,
        IPrototypeManager proto,
        IComponentFactory factory,
        WGBiotype biotype = WGBiotype.Default)
    {
        if (Body(species, biotype, proto, factory) is not { } body
            || !body.Slots.TryGetValue(PartSlot(slot), out var part))
            return false;

        return OrganSlot(slot) is not { } organ || part.Organs.ContainsKey(organ);
    }

    /// <summary>
    /// The body a character is built with: its species' own, or the biotype's when it is converted to a biotype that
    /// isn't its species' (a synthetic human gets the chassis, with no heart to replace).
    /// </summary>
    private static BodyPrototype? Body(string species, WGBiotype biotype, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!proto.TryIndex<SpeciesPrototype>(species, out var speciesProto))
            return null;

        var effective = WGBiotypeRules.Resolve(biotype, speciesProto);
        var overlay = effective == speciesProto.Biotype
            ? null
            : proto.EnumeratePrototypes<WGBiotypePrototype>().FirstOrDefault(o => o.Biotype == effective);
        var id = overlay?.Body
                 ?? (proto.TryIndex(speciesProto.Prototype, out var mob) && mob.TryComp(out BodyComponent? body, factory)
                     ? body.Prototype
                     : null);
        return id is { } bodyId && proto.TryIndex(bodyId, out var bodyProto) ? bodyProto : null;
    }

    /// <summary>The sprite a style draws on a layer, the digitigrade one on digitigrade legs.</summary>
    public static ProtoId<HumanoidSpeciesSpriteLayer>? Sprite(WGAugmentStylePrototype style, HumanoidVisualLayers layer, bool digitigrade)
    {
        if (digitigrade && style.Digitigrade.TryGetValue(layer, out var digi))
            return digi;
        return style.Sprites.TryGetValue(layer, out var sprite) ? sprite : (ProtoId<HumanoidSpeciesSpriteLayer>?) null;
    }

    /// <summary>
    /// The sprite a style draws on a layer over a body whose own limb there is <paramref name="under"/>: the style
    /// fitted to that limb's shape when it differs from the human's, else the human-shaped one.
    /// </summary>
    public static ProtoId<HumanoidSpeciesSpriteLayer>? Sprite(WGAugmentStylePrototype style,
        HumanoidVisualLayers layer,
        bool digitigrade,
        string? under,
        IPrototypeManager proto)
    {
        if (under != null && style.Fitted.Count > 0)
        {
            foreach (var shape in proto.EnumeratePrototypes<WGAugmentShapePrototype>())
            {
                if (style.Fitted.TryGetValue(shape.ID, out var fitted) && shape.BaseSprites.Contains(under))
                    return fitted;
            }
        }

        return Sprite(style, layer, digitigrade);
    }

    /// <summary>The base sprite a body draws on a layer without augments: its leg style's, else its species'.</summary>
    public static string? Underlying(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer, IPrototypeManager proto)
    {
        if (humanoid.LegStyle is { } legStyleId
            && proto.TryIndex(legStyleId, out var legStyle)
            && legStyle.Sprites.TryGetValue(layer, out var legSprite))
            return legSprite;

        if (proto.TryIndex<SpeciesPrototype>(humanoid.Species, out var species)
            && proto.TryIndex<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet, out var set)
            && set.Sprites.TryGetValue(layer, out var sprite))
            return sprite;

        return null;
    }

    /// <summary>Whether a style draws every layer of a slot.</summary>
    public static bool StyleFits(WGAugmentStylePrototype style, WGAugmentSlot slot)
    {
        return Layers(slot).All(style.Sprites.ContainsKey);
    }

    /// <summary>The styles that fit a slot, ordered by prototype id.</summary>
    public static List<WGAugmentStylePrototype> StylesFor(WGAugmentSlot slot, IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WGAugmentStylePrototype>()
            .Where(s => StyleFits(s, slot))
            .OrderBy(s => s.ID)
            .ToList();
    }

    /// <summary>The augments a character of the species and biotype may take in a slot, in order.</summary>
    public static List<WGAugmentPrototype> AugmentsFor(string species,
        WGAugmentSlot slot,
        IPrototypeManager proto,
        IComponentFactory factory,
        WGBiotype biotype = WGBiotype.Default)
    {
        if (!SlotAvailable(species, slot, proto, factory, biotype))
            return new List<WGAugmentPrototype>();

        return proto.EnumeratePrototypes<WGAugmentPrototype>()
            .Where(a => a.Slot == slot && a.Kind != WGAugmentKind.Upgrade && !a.SpeciesBlacklist.Contains(species))
            .OrderBy(a => a.Order)
            .ThenBy(a => a.ID)
            .ToList();
    }

    /// <summary>The implants a character of the species and biotype may take in a slot's part, in order.</summary>
    public static List<WGAugmentPrototype> ImplantsFor(string species,
        WGAugmentSlot slot,
        IPrototypeManager proto,
        IComponentFactory factory,
        WGBiotype biotype = WGBiotype.Default)
    {
        if (!SlotAvailable(species, slot, proto, factory, biotype))
            return new List<WGAugmentPrototype>();

        return proto.EnumeratePrototypes<WGAugmentPrototype>()
            .Where(a => a.Slot == slot && a.Kind == WGAugmentKind.Upgrade && !a.SpeciesBlacklist.Contains(species))
            .OrderBy(a => a.Order)
            .ThenBy(a => a.ID)
            .ToList();
    }

    /// <summary>
    /// How many implants the slot's part holds with these choices: the picked limb's count, plating's if it holds
    /// more than the natural part, none for a missing one or a hand or foot whose limb is missing, one in a trait's
    /// limb, else the natural part's.
    /// </summary>
    public static int ImplantSlots(WGAugmentSlot slot,
        IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice> choices,
        IReadOnlyDictionary<WGAugmentSlot, ProtoId<TraitPrototype>> claimed,
        IPrototypeManager proto)
    {
        if (claimed.ContainsKey(slot))
            return 1;
        if (choices.TryGetValue(slot, out var choice) && proto.TryIndex(choice.Augment, out var augment))
        {
            if (augment.Kind == WGAugmentKind.Limb)
                return augment.ImplantSlots;
            if (augment.Kind == WGAugmentKind.Plating)
                return Math.Max(augment.ImplantSlots, NaturalImplantSlots(slot));
            if (augment.Kind == WGAugmentKind.Missing)
                return 0;
        }
        else if (LimbAbove(slot) is { } above
                 && choices.TryGetValue(above, out var limb)
                 && proto.TryIndex(limb.Augment, out var limbAugment)
                 && limbAugment.Kind == WGAugmentKind.Missing)
        {
            return 0;
        }

        return NaturalImplantSlots(slot);
    }

    /// <summary>
    /// Whether the slot's part is synthetic with these choices: a picked or trait limb or plating is, a hand or foot not picked
    /// on its own is what its limb is, and a natural part is if its prototype is cybernetic or not built of flesh
    /// (an IPC's, or the chassis of a synthetic biotype).
    /// </summary>
    public static bool Synthetic(string species,
        WGAugmentSlot slot,
        IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice> choices,
        IReadOnlyDictionary<WGAugmentSlot, ProtoId<TraitPrototype>> claimed,
        IPrototypeManager proto,
        IComponentFactory factory,
        WGBiotype biotype = WGBiotype.Default)
    {
        if (claimed.ContainsKey(slot))
            return true;
        if (choices.TryGetValue(slot, out var choice)
            && proto.TryIndex(choice.Augment, out var augment)
            && augment.Kind == WGAugmentKind.Limb)
            return augment.Part is not { } part || SyntheticPart(part, proto, factory);
        if (choices.TryGetValue(slot, out var plated)
            && proto.TryIndex(plated.Augment, out var plating)
            && plating.Kind == WGAugmentKind.Plating)
            return true;

        if (LimbAbove(slot) is { } above && (claimed.ContainsKey(above) || choices.ContainsKey(above)))
            return Synthetic(species, above, choices, claimed, proto, factory, biotype);

        return NaturalPart(species, biotype, slot, proto, factory) is { } natural && SyntheticPart(natural, proto, factory);
    }

    private static bool SyntheticPart(EntProtoId part, IPrototypeManager proto, IComponentFactory factory)
    {
        if (!proto.TryIndex(part, out var entity))
            return false;
        if (entity.TryComp(out CyberneticsComponent? _, factory))
            return true;
        return entity.TryComp(out DamageableComponent? damageable, factory)
               && damageable.DamageContainerID is { } container
               && container.Id != "Biological"
               && container.Id != "OrganicPart";
    }

    /// <summary>The part prototype the character's body has in the slot.</summary>
    private static EntProtoId? NaturalPart(string species,
        WGBiotype biotype,
        WGAugmentSlot slot,
        IPrototypeManager proto,
        IComponentFactory factory)
    {
        if (Body(species, biotype, proto, factory) is not { } body
            || !body.Slots.TryGetValue(PartSlot(slot), out var part)
            || part.Part is not { } partId)
            return null;

        return partId;
    }

    /// <summary>Whether an implant goes in a part that is or isn't synthetic.</summary>
    public static bool HostFits(WGAugmentPrototype implant, bool synthetic) => implant.Host switch
    {
        WGAugmentHost.Organic => !synthetic,
        WGAugmentHost.Synthetic => synthetic,
        _ => true,
    };

    /// <summary>
    /// Drops implants the species can't take, that don't fit the part the choices leave in their slot, those past
    /// what the part holds and those that share an exclusive group with one already kept, keeping the first by order.
    /// </summary>
    public static HashSet<ProtoId<WGAugmentPrototype>> ValidateImplants(string species,
        IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice> choices,
        IEnumerable<ProtoId<WGAugmentPrototype>>? implants,
        IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto,
        IComponentFactory factory,
        WGBiotype biotype = WGBiotype.Default)
    {
        var valid = new HashSet<ProtoId<WGAugmentPrototype>>();
        if (implants == null)
            return valid;

        var claimed = ClaimedSlots(traits, proto);
        var used = new Dictionary<WGAugmentSlot, int>();
        var groups = new HashSet<string>();
        var picked = implants
            .Select(id => proto.TryIndex(id, out var augment) ? augment : null)
            .OfType<WGAugmentPrototype>()
            .Where(a => a.Kind == WGAugmentKind.Upgrade)
            .Distinct()
            .OrderBy(a => a.Order)
            .ThenBy(a => a.ID);
        foreach (var implant in picked)
        {
            var slot = implant.Slot;
            if (implant.SpeciesBlacklist.Contains(species)
                || !SlotAvailable(species, slot, proto, factory, biotype)
                || !HostFits(implant, Synthetic(species, slot, choices, claimed, proto, factory, biotype))
                || used.GetValueOrDefault(slot) >= ImplantSlots(slot, choices, claimed, proto)
                || implant.Exclusive is { } group && !groups.Add(group))
                continue;

            used[slot] = used.GetValueOrDefault(slot) + 1;
            valid.Add(implant.ID);
        }

        return valid;
    }

    /// <summary>Whether the profile's legs are digitigrade.</summary>
    public static bool Digitigrade(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        return LegStyleRules.IsDigitigrade(profile.Species, profile.LegStance, proto);
    }

    /// <summary>The slots the given traits put their own parts in, with the trait that claims each.</summary>
    public static Dictionary<WGAugmentSlot, ProtoId<TraitPrototype>> ClaimedSlots(IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto)
    {
        var claimed = new Dictionary<WGAugmentSlot, ProtoId<TraitPrototype>>();
        foreach (var trait in traits)
        {
            if (!proto.TryIndex<WGAugmentTraitSlotsPrototype>(trait.Id, out var slots))
                continue;
            foreach (var slot in slots.Slots)
                claimed.TryAdd(slot, trait);
        }

        return claimed;
    }

    /// <summary>
    /// Drops choices the species or the biotype's body can't take, slots a trait claims, augments that don't belong to
    /// their slot, styles that don't fit and hands or feet under a missing limb, and gives a styled augment without a
    /// style the first one that fits.
    /// </summary>
    public static Dictionary<WGAugmentSlot, WGAugmentChoice> Validate(string species,
        IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice>? choices,
        IEnumerable<ProtoId<TraitPrototype>> traits,
        IPrototypeManager proto,
        IComponentFactory factory,
        WGBiotype biotype = WGBiotype.Default)
    {
        var claimed = ClaimedSlots(traits, proto);
        var valid = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
        if (choices == null)
            return valid;

        foreach (var (slot, choice) in choices)
        {
            if (!Enum.IsDefined(typeof(WGAugmentSlot), slot)
                || claimed.ContainsKey(slot)
                || !proto.TryIndex(choice.Augment, out var augment)
                || augment.Slot != slot
                || augment.Kind == WGAugmentKind.Upgrade
                || augment.SpeciesBlacklist.Contains(species)
                || !SlotAvailable(species, slot, proto, factory, biotype))
                continue;

            ProtoId<WGAugmentStylePrototype>? style = null;
            if (augment.Styled)
            {
                if (choice.Style is { } picked && proto.TryIndex(picked, out var pickedStyle) && StyleFits(pickedStyle, slot))
                    style = picked;
                else if (StylesFor(slot, proto).FirstOrDefault() is { } first)
                    style = first.ID;
                else
                    continue;
            }

            valid[slot] = new WGAugmentChoice(augment.ID, style);
        }

        // A hand or foot can't go on a limb that isn't there.
        foreach (var slot in valid.Keys.ToList())
        {
            if (LimbAbove(slot) is { } above
                && valid.TryGetValue(above, out var limb)
                && proto.TryIndex(limb.Augment, out var limbAugment)
                && limbAugment.Kind == WGAugmentKind.Missing)
                valid.Remove(slot);
        }

        return valid;
    }

    /// <summary>Every augment the profile picked: its slot choices and its implants.</summary>
    public static IEnumerable<ProtoId<WGAugmentPrototype>> Picked(HumanoidCharacterProfile profile)
    {
        return profile.Augments.Values.Select(c => c.Augment).Concat(profile.AugmentImplants);
    }

    /// <summary>The price due for priced augments the profile picked but hasn't unlocked.</summary>
    public static int Due(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        var due = 0;
        foreach (var id in Picked(profile).Distinct())
        {
            if (proto.TryIndex(id, out var augment) && augment.Price > 0 && !profile.AugmentUnlocks.Contains(id))
                due += augment.Price;
        }

        return due;
    }

    /// <summary>The unlocked augments the profile no longer picks, which are sold back or scrapped when it is saved.</summary>
    public static List<WGAugmentPrototype> Sold(HumanoidCharacterProfile profile, IPrototypeManager proto)
    {
        var picked = Picked(profile).ToHashSet();
        return profile.AugmentUnlocks
            .Where(u => !picked.Contains(u))
            .Select(u => proto.TryIndex(u, out var augment) ? augment : null)
            .OfType<WGAugmentPrototype>()
            .OrderBy(a => a.ID)
            .ToList();
    }

    /// <summary>Choices and implants as stored in the database: slot:augment[:style] and Implant:augment entries.</summary>
    public static string ToStored(IReadOnlyDictionary<WGAugmentSlot, WGAugmentChoice> choices,
        IEnumerable<ProtoId<WGAugmentPrototype>> implants)
    {
        var builder = new StringBuilder();
        foreach (var (slot, choice) in choices.OrderBy(c => c.Key))
        {
            if (builder.Length > 0)
                builder.Append(EntrySeparator);
            builder.Append(slot).Append(FieldSeparator).Append(choice.Augment.Id);
            if (choice.Style is { } style)
                builder.Append(FieldSeparator).Append(style.Id);
        }

        foreach (var implant in implants.Select(i => i.Id).Order())
        {
            if (builder.Length > 0)
                builder.Append(EntrySeparator);
            builder.Append(ImplantEntry).Append(FieldSeparator).Append(implant);
        }

        return builder.ToString();
    }

    /// <summary>Reads the slot choices of <see cref="ToStored(IReadOnlyDictionary{WGAugmentSlot, WGAugmentChoice}, IEnumerable{ProtoId{WGAugmentPrototype}})"/> text. Entries it can't read are dropped.</summary>
    public static Dictionary<WGAugmentSlot, WGAugmentChoice> FromStored(string? stored)
    {
        var choices = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
        if (string.IsNullOrEmpty(stored))
            return choices;

        foreach (var entry in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = entry.Split(FieldSeparator);
            if (fields.Length < 2 || !Enum.TryParse<WGAugmentSlot>(fields[0], out var slot))
                continue;

            choices[slot] = new WGAugmentChoice(fields[1], fields.Length > 2 ? fields[2] : null);
        }

        return choices;
    }

    /// <summary>Reads the implants of stored choices, including those saved in the old one-per-slot implant slots.</summary>
    public static HashSet<ProtoId<WGAugmentPrototype>> ImplantsFromStored(string? stored)
    {
        var implants = new HashSet<ProtoId<WGAugmentPrototype>>();
        if (string.IsNullOrEmpty(stored))
            return implants;

        foreach (var entry in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = entry.Split(FieldSeparator);
            if (fields.Length >= 2 && (fields[0] == ImplantEntry || LegacyImplantSlots.Contains(fields[0])))
                implants.Add(fields[1]);
        }

        return implants;
    }

    /// <summary>Unlocked augments as stored in the database: augment or augment:Found entries.</summary>
    public static string ToStored(IEnumerable<ProtoId<WGAugmentPrototype>> unlocks, HashSet<ProtoId<WGAugmentPrototype>> found)
    {
        return string.Join(EntrySeparator,
            unlocks.Select(u => found.Contains(u) ? u.Id + FoundSuffix : u.Id).Order());
    }

    /// <summary>Reads stored unlocks, paid for and found.</summary>
    public static HashSet<ProtoId<WGAugmentPrototype>> UnlocksFromStored(string? stored)
    {
        return UnlocksFromStored(stored, false);
    }

    /// <summary>Reads the stored unlocks that were found. One stored before this was tracked was paid for.</summary>
    public static HashSet<ProtoId<WGAugmentPrototype>> FoundFromStored(string? stored)
    {
        return UnlocksFromStored(stored, true);
    }

    private static HashSet<ProtoId<WGAugmentPrototype>> UnlocksFromStored(string? stored, bool foundOnly)
    {
        var unlocks = new HashSet<ProtoId<WGAugmentPrototype>>();
        if (string.IsNullOrEmpty(stored))
            return unlocks;

        foreach (var entry in stored.Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = entry.Split(FieldSeparator);
            if (!foundOnly || fields.Length > 1 && fields[1] == FoundEntry)
                unlocks.Add(fields[0]);
        }

        return unlocks;
    }
}
