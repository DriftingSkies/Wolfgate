using Content.Shared.Inventory;

namespace Content.Shared._WF.Modsuits.Support;

/// <summary>Multiplies how long the wearer takes to stand up.</summary>
[RegisterComponent]
public sealed partial class ModifyStandingUpTimeComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}

/// <summary>Raised on a body to collect standing-up time modifiers from worn gear.</summary>
[ByRefEvent]
public record struct GetStandingUpTimeMultiplierEvent : IInventoryRelayEvent
{
    /// <summary>The accumulated multiplier. A fresh event starts at one.</summary>
    public float Multiplier;

    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;

    public GetStandingUpTimeMultiplierEvent()
    {
        Multiplier = 1f;
    }
}

public sealed partial class MultiplyStandingUpTimeSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModifyStandingUpTimeComponent, GetStandingUpTimeMultiplierEvent>(OnGetMultiplier);
        SubscribeLocalEvent<ModifyStandingUpTimeComponent, InventoryRelayedEvent<GetStandingUpTimeMultiplierEvent>>(OnInventoryGetMultiplier);
    }

    private void OnGetMultiplier(Entity<ModifyStandingUpTimeComponent> ent, ref GetStandingUpTimeMultiplierEvent args)
    {
        args.Multiplier *= ent.Comp.Multiplier;
    }

    private void OnInventoryGetMultiplier(Entity<ModifyStandingUpTimeComponent> ent,
        ref InventoryRelayedEvent<GetStandingUpTimeMultiplierEvent> args)
    {
        args.Args.Multiplier *= ent.Comp.Multiplier;
    }
}
