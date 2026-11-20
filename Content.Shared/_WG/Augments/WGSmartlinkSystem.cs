using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Emp;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;

namespace Content.Shared._WG.Augments;

/// <summary>
/// Compensates for the recoil of a gun held in a smartlinked hand: its spread grows slower as it fires and settles
/// sooner. An EMP scrambles the link until it wears off, and the gun then does worse than unlinked.
/// </summary>
public sealed class WGSmartlinkSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSmartlinkComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WGSmartlinkComponent, BodyPartComponentsModifyEvent>(OnPartModify);
        SubscribeLocalEvent<WGSmartlinkComponent, EmpPulseEvent>(OnEmpPulse);
        SubscribeLocalEvent<WGSmartlinkComponent, EmpDisabledRemovedEvent>(OnEmpOver);
        SubscribeLocalEvent<WGSmartlinkUserComponent, GunRefreshModifiersEvent>(OnRefresh);
    }

    private void OnStartup(Entity<WGSmartlinkComponent> ent, ref ComponentStartup args)
    {
        if (CompOrNull<BodyPartComponent>(ent)?.Body is not { } body)
            return;

        Mark(body, ent, true);
        RefreshHeld(ent);
    }

    // Raised on the hand as it joins or leaves a body, and as it is switched on or off.
    private void OnPartModify(Entity<WGSmartlinkComponent> ent, ref BodyPartComponentsModifyEvent args)
    {
        Mark(args.Body, ent, args.Add);
    }

    private void OnEmpPulse(Entity<WGSmartlinkComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;
        SetScrambled(ent, true);
    }

    private void OnEmpOver(Entity<WGSmartlinkComponent> ent, ref EmpDisabledRemovedEvent args)
    {
        if (LifeStage(ent) < EntityLifeStage.Terminating)
            SetScrambled(ent, false);
    }

    private void OnRefresh(Entity<WGSmartlinkUserComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (LinkFor(ent, args.Gun) is not { } link)
            return;

        args.AngleIncrease *= link.Scrambled ? link.ScrambledIncreaseMultiplier : link.IncreaseMultiplier;
        args.AngleDecay *= link.Scrambled ? link.ScrambledDecayMultiplier : link.DecayMultiplier;
        args.CameraRecoilScalar *= link.Scrambled ? link.ScrambledCameraRecoilMultiplier : link.CameraRecoilMultiplier;
        if (!link.Scrambled)
            args.ProjectileSpeed *= link.ProjectileSpeedMultiplier;
    }

    /// <summary>The link in the hand of a body that holds a gun, if that hand has a working one.</summary>
    public WGSmartlinkComponent? LinkFor(EntityUid body, EntityUid gun)
    {
        if (!HasComp<WGSmartlinkUserComponent>(body) || !_hands.IsHolding(body, gun, out var hand))
            return null;

        return Link(body, hand.Name);
    }

    private void SetScrambled(Entity<WGSmartlinkComponent> ent, bool scrambled)
    {
        if (ent.Comp.Scrambled == scrambled)
            return;

        ent.Comp.Scrambled = scrambled;
        Dirty(ent);
        RefreshHeld(ent);
    }

    /// <summary>Keeps <see cref="WGSmartlinkUserComponent"/> on a body for as long as one of its hands is linked.</summary>
    private void Mark(EntityUid body, EntityUid hand, bool linked)
    {
        if (_net.IsClient || TerminatingOrDeleted(body))
            return;

        if (linked)
        {
            EnsureComp<WGSmartlinkUserComponent>(body);
            return;
        }

        foreach (var (other, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Hand))
        {
            if (other != hand && HasComp<WGSmartlinkComponent>(other))
                return;
        }

        RemComp<WGSmartlinkUserComponent>(body);
    }

    /// <summary>The working link in the part behind one of a body's hands.</summary>
    private WGSmartlinkComponent? Link(EntityUid body, string handName)
    {
        foreach (var (id, part) in _body.GetBodyChildrenOfType(body, BodyPartType.Hand))
        {
            if (part.Enabled && HandName(part) == handName && TryComp(id, out WGSmartlinkComponent? link))
                return link;
        }

        return null;
    }

    /// <summary>Has the gun in a linked hand work its spread out again.</summary>
    private void RefreshHeld(EntityUid hand)
    {
        if (!TryComp(hand, out BodyPartComponent? part)
            || part.Body is not { } body
            || HandName(part) is not { } name
            || !_hands.TryGetHand(body, name, out var held)
            || !TryComp(held.HeldEntity, out GunComponent? gun))
            return;

        _gun.RefreshModifiers((held.HeldEntity.Value, gun));
    }

    private static string? HandName(BodyPartComponent part)
    {
        return part.ParentSlot is { } slot ? SharedBodySystem.GetPartSlotContainerId(slot.Id) : null;
    }
}
