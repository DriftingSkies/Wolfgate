using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._WF.ArmorDeflection;
using Content.Shared._WF.ArmorSpecialization;
using Content.Shared._WF.NeckSuits;
using Content.Shared._WF.RMCClothing.Ghillie;
using Content.Shared._WF.SoftShield;
using Content.Shared._WF.SuitTank;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Examine;
using Content.Shared.Explosion.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Slippery;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Stunnable;
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
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
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
        typeof(FireProtectionComponent),
        typeof(ProtectedFromStepTriggersComponent),
        typeof(DamageOnInteractProtectionComponent),
        typeof(NoSlipComponent),
        typeof(WFArmorDeflectComponent),
        typeof(WFWoundWardComponent),
    };

    /// <summary>
    /// Suits with any of these are functional only, as Meridian Rift's <c>only_functional</c>: space suits, and
    /// suits whose effect is more than stats (reflection, shields, stealth, tank storage, armor specializations).
    /// </summary>
    private static readonly Type[] FunctionalOnly =
    {
        typeof(PressureProtectionComponent),
        typeof(ReflectComponent),
        typeof(WFSoftShieldComponent),
        typeof(WFGhillieSuitComponent),
        typeof(WFSuitTankStorageComponent),
        typeof(WFLimbLockComponent),
        typeof(WFPainDampenerComponent),
        typeof(WFAblativeCoatComponent),
        typeof(WFAutoTourniquetComponent),
        typeof(WFPlasmaReserveComponent),
        typeof(WFSteadyHandsComponent),
        typeof(WFSterileWeaveComponent),
        typeof(WFStasisWeaveComponent),
        typeof(WFServoAssistComponent),
        typeof(WFRadSinkComponent),
        typeof(WFEmpHardenedComponent),
        typeof(WFHaulFrameComponent),
        typeof(WFWildlifePaddingComponent),
        typeof(WFBlastLinerComponent),
        typeof(WFSuitThrustersComponent),
        typeof(WFLivingTissueComponent),
        typeof(WFHonkOnHitComponent),
        typeof(WFVowOfSilenceComponent),
        typeof(WFDeconCoatingComponent),
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<ClothingComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>Whether this suit can be adjusted: an outer suit, or one already in neck mode.</summary>
    public bool CanAdjust(Entity<ClothingComponent> ent)
    {
        if (HasComp<WFNeckSuitComponent>(ent))
            return true;

        if ((ent.Comp.Slots & SlotFlags.OUTERCLOTHING) == 0)
            return false;

        foreach (var type in FunctionalOnly)
        {
            if (HasComp(ent, type))
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

    /// <summary>
    /// Adjusts the suit for neck (non-functional) or suit (functional) use. Refused while it's worn, as on Meridian
    /// Rift, so a suit never changes slot or protection while in one.
    /// </summary>
    public bool SetNeckMode(Entity<ClothingComponent> ent, bool neck, EntityUid? user = null)
    {
        if (HasComp<WFNeckSuitComponent>(ent) == neck || _inventory.TryGetContainingSlot(ent.Owner, out _))
            return false;

        if (neck)
        {
            foreach (var type in Functional)
            {
                if (HasComp(ent, type))
                    RemComp(ent, type);
            }

            // WFNeckSuitSlotsSystem moves it to the neck slot, on the client too.
            AddComp<WFNeckSuitComponent>(ent);
        }
        else
        {
            if (MetaData(ent).EntityPrototype is { } proto)
            {
                foreach (var type in Functional)
                {
                    if (proto.Components.TryGetValue(_factory.GetComponentName(type), out var entry))
                        EntityManager.AddComponent(ent, entry, overwrite: true);
                }
            }

            RemComp<WFNeckSuitComponent>(ent);
        }

        // A held suit's slowdown is mirrored onto its holder.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var holder))
            _speed.RefreshMovementSpeedModifiers(holder.Value);

        if (user != null)
        {
            _popup.PopupEntity(Loc.GetString(neck ? "wf-neck-suit-popup-cosmetic" : "wf-neck-suit-popup-functional",
                ("item", ent.Owner)), user.Value, user.Value);
        }

        return true;
    }
}
