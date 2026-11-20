using System.Linq;
using System.Numerics;
using Content.Shared._Goobstation.Autosurgeon;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared._WG.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Augments;

/// <summary>
/// Swaps a body's parts and organs for the augments its profile picked, then puts its implants in. The old part or
/// organ is cut loose from the body before it is deleted, so its loss doesn't read as an injury; a hand keeps what it
/// holds.
/// </summary>
public sealed partial class WGAugmentSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private AutoSurgeonSystem _autoSurgeon = default!;
    [Dependency] private IComponentFactory _factory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BodyComponent, WGApplyAugmentsEvent>(OnApplyAugments);
        InitializePersistence();
        InitializePaid();
        InitializeScavenge();
    }

    private void OnApplyAugments(Entity<BodyComponent> ent, ref WGApplyAugmentsEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.MapInitialized || TerminatingOrDeleted(ent))
            return;

        var digitigrade = WGAugmentRules.Digitigrade(args.Profile, _proto);
        foreach (var (slot, choice) in args.Profile.Augments.OrderBy(a => a.Key))
        {
            if (!_proto.TryIndex(choice.Augment, out var augment))
                continue;

            switch (augment.Kind)
            {
                case WGAugmentKind.Limb when augment.Part is { } part:
                    if (ReplaceLimb(ent, slot, part) is { } limb && choice.Style is { } style && _proto.TryIndex(style, out var styleProto))
                        StyleLimb(ent, limb, styleProto, digitigrade);
                    break;
                case WGAugmentKind.Missing:
                    if (FindLimb(ent, slot) is { } missing)
                        CutLoose(ent, missing, keepHands: false);
                    break;
                case WGAugmentKind.Organ when augment.Organ is { } organ:
                    ReplaceOrgan(ent, slot, organ);
                    break;
            }
        }

        // Implants go in after the limbs they sit in.
        foreach (var id in args.Profile.AugmentImplants.OrderBy(i => i.Id))
        {
            if (_proto.TryIndex(id, out var implant) && Surgeon(implant) is { } surgeon)
                _autoSurgeon.WgApplyUpgrade(ent, surgeon);
        }
    }

    private (EntityUid Id, BodyPartComponent Component)? FindLimb(Entity<BodyComponent> body, WGAugmentSlot slot)
    {
        var (type, symmetry) = slot switch
        {
            WGAugmentSlot.LeftArm => (BodyPartType.Arm, BodyPartSymmetry.Left),
            WGAugmentSlot.RightArm => (BodyPartType.Arm, BodyPartSymmetry.Right),
            WGAugmentSlot.LeftLeg => (BodyPartType.Leg, BodyPartSymmetry.Left),
            WGAugmentSlot.RightLeg => (BodyPartType.Leg, BodyPartSymmetry.Right),
            WGAugmentSlot.LeftHand => (BodyPartType.Hand, BodyPartSymmetry.Left),
            WGAugmentSlot.RightHand => (BodyPartType.Hand, BodyPartSymmetry.Right),
            WGAugmentSlot.LeftFoot => (BodyPartType.Foot, BodyPartSymmetry.Left),
            WGAugmentSlot.RightFoot => (BodyPartType.Foot, BodyPartSymmetry.Right),
            _ => (BodyPartType.Other, BodyPartSymmetry.None),
        };
        if (type == BodyPartType.Other)
            return null;

        foreach (var part in _body.GetBodyChildrenOfType(body, type, body.Comp, symmetry))
            return part;
        return null;
    }

    /// <summary>The part a missing limb in the slot would hang from: the torso, or the arm or leg for a hand or foot.</summary>
    private EntityUid? LimbParent(Entity<BodyComponent> body, WGAugmentSlot slot)
    {
        var limb = slot switch
        {
            WGAugmentSlot.LeftHand => WGAugmentSlot.LeftArm,
            WGAugmentSlot.RightHand => WGAugmentSlot.RightArm,
            WGAugmentSlot.LeftFoot => WGAugmentSlot.LeftLeg,
            WGAugmentSlot.RightFoot => WGAugmentSlot.RightLeg,
            _ => (WGAugmentSlot?) null,
        };
        if (limb != null)
            return FindLimb(body, limb.Value)?.Id;

        var torso = _body.GetBodyChildrenOfType(body, BodyPartType.Torso, body.Comp).FirstOrDefault().Id;
        return torso.Valid ? torso : null;
    }

    /// <summary>The autosurgeon component an upgrade augment installs from.</summary>
    private AutoSurgeonComponent? Surgeon(WGAugmentPrototype augment)
    {
        return augment.Autosurgeon is { } id
               && _proto.TryIndex(id, out var entity)
               && entity.TryComp(out AutoSurgeonComponent? surgeon, _factory)
            ? surgeon
            : null;
    }

    /// <summary>Replaces the slot's limb with a fresh one of the given prototype; the new limb, or null if it failed.</summary>
    private EntityUid? ReplaceLimb(Entity<BodyComponent> body, WGAugmentSlot slot, EntProtoId proto)
    {
        var slotId = WGAugmentRules.PartSlot(slot);
        EntityUid? parent;
        if (FindLimb(body, slot) is { } old)
        {
            if (MetaData(old.Id).EntityPrototype?.ID == proto.Id)
                return old.Id;

            parent = _body.GetParentPartOrNull(old.Id);
            slotId = old.Component.ParentSlot?.Id ?? slotId;
            CutLoose(body, old, keepHands: true);
        }
        else
        {
            parent = LimbParent(body, slot);
        }

        if (parent is not { Valid: true } parentUid || !TryComp(parentUid, out BodyPartComponent? parentPart))
            return null;

        var limb = Spawn(proto, new EntityCoordinates(body, Vector2.Zero));
        if (!TryComp(limb, out BodyPartComponent? limbPart)
            || !_body.AttachPart(parentUid, slotId, limb, parentPart, limbPart))
        {
            Del(limb);
            return null;
        }

        return limb;
    }

    /// <summary>Gives a limb and the parts under it the style's sprites, so a severed one keeps its look.</summary>
    private void StyleLimb(EntityUid body, EntityUid limb, WGAugmentStylePrototype style, bool digitigrade)
    {
        TryComp(body, out HumanoidAppearanceComponent? humanoid);
        foreach (var (part, comp) in _body.GetBodyPartChildren(limb))
        {
            if (comp.ToHumanoidLayers() is not { } layer)
                continue;

            var under = humanoid == null ? null : WGAugmentRules.Underlying(humanoid, layer, _proto);
            if (WGAugmentRules.Sprite(style, layer, digitigrade, under, _proto) is not { } sprite)
                continue;

            var appearance = EnsureComp<BodyPartAppearanceComponent>(part);
            appearance.Type = layer;
            appearance.ID = sprite;
            appearance.Color = null;
            Dirty(part, appearance);
        }
    }

    /// <summary>Takes a limb off without it reading as an injury. A replaced limb's hand keeps what it holds.</summary>
    private void CutLoose(Entity<BodyComponent> body, (EntityUid Id, BodyPartComponent Component) limb, bool keepHands)
    {
        if (!keepHands)
        {
            foreach (var (_, comp) in _body.GetBodyPartChildren(limb.Id, limb.Component))
            {
                if (comp.PartType == BodyPartType.Hand && comp.ParentSlot is { } handSlot)
                    _hands.RemoveHand(body, SharedBodySystem.GetPartSlotContainerId(handSlot.Id));
            }
        }

        _body.WgCutLoose(body, limb.Id, body.Comp, limb.Component);
    }

    private void ReplaceOrgan(Entity<BodyComponent> body, WGAugmentSlot slot, EntProtoId proto)
    {
        if (WGAugmentRules.OrganSlot(slot) is not { } organSlot)
            return;

        var partType = WGAugmentRules.PartSlot(slot) == "head" ? BodyPartType.Head : BodyPartType.Torso;
        if (_body.GetBodyChildrenOfType(body, partType, body.Comp).FirstOrDefault() is not { Id.Valid: true } part
            || !_containers.TryGetContainer(part.Id, SharedBodySystem.GetOrganContainerId(organSlot), out var container))
            return;

        foreach (var old in container.ContainedEntities.ToList())
        {
            if (MetaData(old).EntityPrototype?.ID == proto.Id)
                return;

            _body.WgDeleteOrgan(body, old);
        }

        var organ = Spawn(proto, new EntityCoordinates(body, Vector2.Zero));
        if (!_body.InsertOrgan(part.Id, organ, organSlot, part.Component))
            Del(organ);
    }

}
