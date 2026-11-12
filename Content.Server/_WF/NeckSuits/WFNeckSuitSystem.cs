using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared.Stunnable;
using Content.Shared._WF.ArmorDeflection;
using Content.Shared._WF.ArmorSpecialization;
using Content.Shared._WF.NeckSuits;
using Content.Shared._WF.SoftShield;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Explosion.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WF.NeckSuits;

/// <summary>
/// Meridian Rift's functional toggle: a held outer suit can be adjusted into a cosmetic neck piece and back.
/// </summary>
public sealed partial class WFNeckSuitSystem : EntitySystem
{
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly SpriteSpecifier VerbIcon =
        new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/fold.svg.192dpi.png"));

    /// <summary>What the suit loses in neck mode, and gets back from its prototype after.</summary>
    private static readonly Type[] Functional =
    {
        typeof(ArmorComponent),
        typeof(TemperatureProtectionComponent),
        typeof(ClothingSpeedModifierComponent),
        typeof(ExplosionResistanceComponent),
        typeof(StaminaDamageResistanceComponent),
        typeof(WFArmorDeflectComponent),
        typeof(WFWoundWardComponent),
    };

    private const string SpecializationNamespace = "Content.Shared._WF.ArmorSpecialization";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ClothingComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    /// Whether this outer suit can be adjusted. Space suits and suits with special effects (reflection, shields,
    /// armor specializations) are functional only, as on Meridian Rift.
    /// </summary>
    public bool CanAdjust(Entity<ClothingComponent> ent)
    {
        if (HasComp<WFNeckSuitComponent>(ent))
            return true;

        if ((ent.Comp.Slots & SlotFlags.OUTERCLOTHING) == 0
            || HasComp<PressureProtectionComponent>(ent)
            || HasComp<ReflectComponent>(ent)
            || HasComp<WFSoftShieldComponent>(ent))
        {
            return false;
        }

        foreach (var comp in EntityManager.GetComponents(ent))
        {
            var type = comp.GetType();
            if (type.Namespace == SpecializationNamespace && type != typeof(WFWoundWardComponent))
                return false;
        }

        return true;
    }

    private void OnExamined(Entity<ClothingComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !CanAdjust(ent))
            return;

        args.PushMarkup(Loc.GetString(HasComp<WFNeckSuitComponent>(ent)
            ? "wf-neck-suit-examine-neck"
            : "wf-neck-suit-examine-suit"));
    }

    private void OnGetVerbs(Entity<ClothingComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null
            || !_hands.IsHolding((args.User, args.Hands), ent) || !CanAdjust(ent))
        {
            return;
        }

        var user = args.User;
        var neck = HasComp<WFNeckSuitComponent>(ent);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(neck ? "wf-neck-suit-verb-functional" : "wf-neck-suit-verb-cosmetic"),
            Icon = VerbIcon,
            Act = () => SetNeckMode(ent, !neck, user),
        });
    }

    /// <summary>Adjusts the suit for neck (non-functional) or suit (functional) use.</summary>
    public void SetNeckMode(Entity<ClothingComponent> ent, bool neck, EntityUid? user = null)
    {
        if (HasComp<WFNeckSuitComponent>(ent) == neck)
            return;

        var proto = MetaData(ent).EntityPrototype;
        if (neck)
        {
            foreach (var type in Functional)
            {
                if (HasComp(ent, type))
                    RemComp(ent, type);
            }

            _clothing.SetSlots(ent, SlotFlags.NECK, ent.Comp);
            AddComp<WFNeckSuitComponent>(ent);
        }
        else
        {
            var slots = SlotFlags.OUTERCLOTHING;
            if (proto != null)
            {
                if (proto.Components.TryGetValue(_factory.GetComponentName<ClothingComponent>(), out var clothing))
                    slots = ((ClothingComponent) clothing.Component).Slots;

                foreach (var type in Functional)
                {
                    if (proto.Components.TryGetValue(_factory.GetComponentName(type), out var entry))
                        EntityManager.AddComponent(ent, entry, overwrite: true);
                }
            }

            _clothing.SetSlots(ent, slots, ent.Comp);
            RemComp<WFNeckSuitComponent>(ent);
        }

        if (user == null)
            return;

        _popup.PopupEntity(Loc.GetString(neck ? "wf-neck-suit-popup-cosmetic" : "wf-neck-suit-popup-functional",
            ("item", ent.Owner)), user.Value, user.Value);
    }
}
