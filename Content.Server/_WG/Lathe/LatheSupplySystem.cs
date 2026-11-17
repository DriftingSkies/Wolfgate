using Content.Server.Administration.Logs;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WG.Lathe;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Tools.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Lathe;

/// <summary>
/// Loads recipe parts into lathes by use and lets players change a queued batch's total.
/// </summary>
public sealed partial class LatheSupplySystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private LatheSystem _lathe = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LatheComponent, InteractUsingEvent>(OnInteractUsing,
            before: new[] { typeof(MaterialStorageSystem) });
        SubscribeLocalEvent<LatheComponent, LatheRecipeAmountMessage>(OnRecipeAmount);
        Subs.BuiEvents<LatheComponent>(LatheUiKey.Key,
            subs => subs.Event<BoundUIOpenedEvent>(OnUiOpened));
    }

    private void OnInteractUsing(Entity<LatheComponent> ent, ref InteractUsingEvent args)
    {
        // Tools work the machine itself; tool ingredients go in through the storage compartment.
        if (args.Handled ||
            HasComp<ToolComponent>(args.Used) ||
            !TryComp<EntityStorageComponent>(ent, out var storage) ||
            MetaData(args.Used).EntityPrototype?.ID is not { } part ||
            !UsesPart(ent, part))
            return;

        // Close first; an open compartment drops inserted items beside the lathe.
        if (storage.Open && !_entityStorage.TryCloseStorage(ent))
            return;

        if (!_entityStorage.CanInsert(args.Used, ent, storage) ||
            !_entityStorage.AddToContents(args.Used, ent, storage))
            return;

        args.Handled = true;
        _lathe.TryStartProducing(ent, ent.Comp);
        _lathe.UpdateUserInterfaceState(ent, ent.Comp);
    }

    private void OnRecipeAmount(Entity<LatheComponent> ent, ref LatheRecipeAmountMessage args)
    {
        var index = args.Index;
        var batchIndex = ent.Comp.Queue.FindIndex(batch => batch.Index == index);
        if (batchIndex < 0)
            return;

        var batch = ent.Comp.Queue[batchIndex];
        if (args.Amount <= 0 ||
            args.Amount > LatheRecipeBatch.MaxItemsRequested ||
            args.Amount < batch.ItemsPrinted)
            return;

        // A batch can always shrink, but only grow while the design is still available.
        if (args.Amount > batch.ItemsRequested &&
            !_lathe.GetAvailableRecipes(ent, ent.Comp).Contains(batch.Recipe.ID))
            return;

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Actor):player} changed a {_lathe.GetRecipeName(batch.Recipe)} batch from {batch.ItemsRequested} to {args.Amount} at {ToPrettyString(ent):lathe}");

        if (args.Amount == batch.ItemsPrinted)
            ent.Comp.Queue.RemoveAt(batchIndex);
        else
            batch.ItemsRequested = args.Amount;

        _lathe.TryStartProducing(ent, ent.Comp);
        _lathe.UpdateUserInterfaceState(ent, ent.Comp);
    }

    private void OnUiOpened(Entity<LatheComponent> ent, ref BoundUIOpenedEvent args)
    {
        _lathe.UpdateUserInterfaceState(ent, ent.Comp);
    }

    private bool UsesPart(Entity<LatheComponent> ent, EntProtoId part)
    {
        foreach (var id in _lathe.GetAvailableRecipes(ent, ent.Comp))
        {
            if (_proto.Index(id).Entities.ContainsKey(part))
                return true;
        }

        return false;
    }
}
