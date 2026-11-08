using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Splits or evolves <see cref="FaunaFeedComponent"/> mobs when fed the right item.
/// </summary>
public sealed partial class FaunaFeedSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaFeedComponent, InteractUsingEvent>(OnInteractUsing);
    }

    private void OnInteractUsing(Entity<FaunaFeedComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_mobState.IsAlive(ent))
            return;

        TryComp<StackComponent>(args.Used, out var stack);
        var evolve = ent.Comp.EvolveInto != null
            && Matches(args.Used, stack, ent.Comp.EvolveKey, ent.Comp.EvolveStacks);
        if (!evolve && !Matches(args.Used, stack, ent.Comp.Food, ent.Comp.FoodStacks))
            return;

        args.Handled = true;
        if (stack != null)
            _stack.Use(args.Used, 1, stack);
        else
            QueueDel(args.Used);

        var coords = Transform(ent).Coordinates;
        if (evolve)
        {
            _popup.PopupCoordinates(Loc.GetString("fauna-feed-evolve", ("user", ent.Owner)), coords, PopupType.Medium);
            Spawn(ent.Comp.EvolveInto, coords);
            QueueDel(ent);
            return;
        }

        _popup.PopupEntity(Loc.GetString("fauna-feed-split", ("user", ent.Owner)), ent, PopupType.Medium);
        Spawn(MetaData(ent).EntityPrototype?.ID, coords);
    }

    private bool Matches(EntityUid item,
        StackComponent? stack,
        EntityWhitelist? whitelist,
        List<ProtoId<StackPrototype>> stacks)
    {
        if (stack != null && stacks.Contains(stack.StackTypeId))
            return true;

        return _whitelist.IsWhitelistPass(whitelist, item);
    }
}
