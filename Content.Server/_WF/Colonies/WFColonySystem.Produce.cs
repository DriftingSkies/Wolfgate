using Content.Server._Crescent.Dispenser;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Shared._WF.Colonies;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Colonies;

/// <summary>
/// Produce at the colony exchange: what a farm really yields, crops, eggs, hides, and milk and wool poured in from a
/// bucket, sells at the exchange chute for what it is worth. It goes on the colony's shelf as that share of a crate of
/// Supplies or Fabric, and scores for the seller's holder like anything else put on the shelf.
/// </summary>
public sealed partial class WFColonySystem
{
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly EntProtoId ProduceCash = "SpaceCash";
    private static readonly SoundSpecifier ProduceSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    /// <summary>The live price over the table price a produce sale may follow, either way.</summary>
    private const float ProduceSwing = 0.5f;

    private void InitializeProduce()
    {
        SubscribeLocalEvent<WFColonyProduceChuteComponent, InteractUsingEvent>(OnProduceUsed, before: new[] { typeof(DispenserSystem) });
    }

    private void OnProduceUsed(Entity<WFColonyProduceChuteComponent> chute, ref InteractUsingEvent args)
    {
        if (args.Handled || _market.MarketOf(chute) is not { } market || !TryComp<WFColonyComponent>(market, out var colony))
            return;

        if (TrySellProduce(chute, (market, colony), args.Used, args.User, out var paid))
        {
            args.Handled = true;
            _audio.PlayPvs(ProduceSound, chute);
            _popup.PopupEntity(Loc.GetString("wf-colony-produce-sold", ("amount", paid)), chute, args.User);
        }
    }

    /// <summary>
    /// Sells an item of produce, or the produce poured out of a container, at a colony's exchange chute: the seller is
    /// paid in cash at the chute, the shelf takes its worth in crates and the seller's holder scores for them.
    /// </summary>
    public bool TrySellProduce(EntityUid chute, Entity<WFColonyComponent> colony, EntityUid item, EntityUid seller, out int paid)
    {
        paid = 0;
        foreach (var produce in _proto.EnumeratePrototypes<WFColonyProducePrototype>())
        {
            var worth = ProduceWorth(item, produce, out var consumed, out var pour);
            if (worth <= 0 || ChutePrice(produce.Good) is not { } crate || crate <= 0)
                continue;

            // The exchange pays by its shelf, as it does for crates, within a band of what the produce is worth.
            var factor = _market.Quote(chute, produce.Good) is { } quote
                ? Math.Clamp((float) quote.Amount / crate, 1f - ProduceSwing, 1f + ProduceSwing)
                : 1f;
            paid = (int) Math.Floor(worth * factor);
            if (paid <= 0)
                return false;

            pour?.Invoke();
            if (consumed)
                QueueDel(item);

            var crates = (float) (worth / crate);
            _market.Row(colony, produce.Good);
            _market.AddStock(colony, produce.Good, crates);
            _stack.SpawnMultiple(ProduceCash, paid, Transform(chute).Coordinates);
            if (TryComp<ActorComponent>(seller, out var actor))
            {
                var holder = HolderOf(actor.PlayerSession, out var name);
                AddPoints(colony.Comp.World, holder, name, crates);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// What an item is worth as a kind of produce, in thalers: its own price when it is the produce, or the listed
    /// reagents in the container it is. <paramref name="pour"/> takes those reagents out, leaving the container.
    /// </summary>
    private double ProduceWorth(EntityUid item, WFColonyProducePrototype produce, out bool consumed, out Action? pour)
    {
        consumed = false;
        pour = null;
        if (IsProduce(item, produce))
        {
            consumed = true;
            var count = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
            return Math.Max(_pricing.GetPrice(item), produce.Value * count);
        }

        if (produce.Reagents.Count == 0 || !_solutions.TryGetDrainableSolution(item, out var soln, out var solution))
            return 0;

        var worth = 0.0;
        var held = soln.Value;
        foreach (var reagent in produce.Reagents)
        {
            var amount = solution.GetTotalPrototypeQuantity(reagent);
            if (amount <= 0)
                continue;

            worth += amount.Float() * produce.ReagentValue;
            var id = reagent.Id;
            pour += () => _solutions.RemoveReagent(held, id, amount);
        }

        return worth;
    }

    private bool IsProduce(EntityUid item, WFColonyProducePrototype produce)
    {
        if (MetaData(item).EntityPrototype is { } proto && produce.Prototypes.Contains(proto.ID))
            return true;

        foreach (var tag in produce.Tags)
        {
            if (_tags.HasTag(item, tag))
                return true;
        }

        foreach (var name in produce.Components)
        {
            if (ComponentType(name) is { } type && HasComp(item, type))
                return true;
        }

        return false;
    }
}
