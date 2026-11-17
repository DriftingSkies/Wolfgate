using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._WG.ArmorDeflection;
using Content.Shared._WG.ArmorSpecialization;
using Content.Shared._WG.NeckSuits;
using Content.Shared._WG.RMCClothing.Ghillie;
using Content.Shared._WG.SoftShield;
using Content.Shared._WG.SuitTank;
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

namespace Content.Server._WG.NeckSuits;

/// <summary>
/// Meridian Rift's functional toggle: a held outer suit can be adjusted into a cosmetic neck piece and back.
/// </summary>
public sealed partial class WGNeckSuitSystem : EntitySystem
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
        typeof(WGArmorDeflectComponent),
        typeof(WGWoundWardComponent),
    };

    /// <summary>
    /// Suits with any of these are functional only, as Meridian Rift's <c>only_functional</c>: space suits, and
    /// suits whose effect is more than stats (reflection, shields, stealth, tank storage, armor specializations).
    /// </summary>
    private static readonly Type[] FunctionalOnly =
    {
        typeof(PressureProtectionComponent),
        typeof(ReflectComponent),
        typeof(WGSoftShieldComponent),
        typeof(WGGhillieSuitComponent),
        typeof(WGSuitTankStorageComponent),
        typeof(WGLimbLockComponent),
        typeof(WGPainDampenerComponent),
        typeof(WGAblativeCoatComponent),
        typeof(WGAutoTourniquetComponent),
        typeof(WGPlasmaReserveComponent),
        typeof(WGSteadyHandsComponent),
        typeof(WGSterileWeaveComponent),
        typeof(WGStasisWeaveComponent),
        typeof(WGServoAssistComponent),
        typeof(WGRadSinkComponent),
        typeof(WGEmpHardenedComponent),
        typeof(WGHaulFrameComponent),
        typeof(WGWildlifePaddingComponent),
        typeof(WGBlastLinerComponent),
        typeof(WGSuitThrustersComponent),
        typeof(WGLivingTissueComponent),
        typeof(WGHonkOnHitComponent),
        typeof(WGVowOfSilenceComponent),
        typeof(WGDeconCoatingComponent),
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
        if (HasComp<WGNeckSuitComponent>(ent))
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

        args.PushMarkup(Loc.GetString(HasComp<WGNeckSuitComponent>(ent)
            ? "wg-neck-suit-examine-neck"
            : "wg-neck-suit-examine-suit"));
    }

    private void OnGetVerbs(Entity<ClothingComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null
            || !_hands.IsHolding((args.User, args.Hands), ent) || !CanAdjust(ent))
        {
            return;
        }

        var user = args.User;
        var neck = HasComp<WGNeckSuitComponent>(ent);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(neck ? "wg-neck-suit-verb-functional" : "wg-neck-suit-verb-cosmetic"),
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
        if (HasComp<WGNeckSuitComponent>(ent) == neck || _inventory.TryGetContainingSlot(ent.Owner, out _))
            return false;

        if (neck)
        {
            foreach (var type in Functional)
            {
                if (HasComp(ent, type))
                    RemComp(ent, type);
            }

            // WGNeckSuitSlotsSystem moves it to the neck slot, on the client too.
            AddComp<WGNeckSuitComponent>(ent);
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

            RemComp<WGNeckSuitComponent>(ent);
        }

        // A held suit's slowdown is mirrored onto its holder.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var holder))
            _speed.RefreshMovementSpeedModifiers(holder.Value);

        if (user != null)
        {
            _popup.PopupEntity(Loc.GetString(neck ? "wg-neck-suit-popup-cosmetic" : "wg-neck-suit-popup-functional",
                ("item", ent.Owner)), user.Value, user.Value);
        }

        return true;
    }
}
