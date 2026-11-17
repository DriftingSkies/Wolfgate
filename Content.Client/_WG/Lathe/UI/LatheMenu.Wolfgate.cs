using System.Linq;
using Content.Client._WG.Lathe;
using Content.Client._WG.Lathe.UI;
using Content.Shared._WG.Lathe;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Lathe.UI;

public sealed partial class LatheMenu
{
    /// <summary>
    /// Raised with a batch index and its new requested total.
    /// </summary>
    public event Action<int, int>? OnRecipeAmountChanged;

    private readonly Dictionary<int, LatheQueueEntry> _queueEntries = new();
    private Label? _queueEmptyLabel;
    private LatheUpdateState? _wolfgateState;
    private FabricationSiloSystem? _fabricationSilo;

    private FabricationSiloSystem FabricationSilo => _fabricationSilo ??= _entityManager.System<FabricationSiloSystem>();

    /// <summary>
    /// Takes the server's readiness from a state update; silo links and stock are read from the networked silos.
    /// </summary>
    public void SetWolfgateState(LatheUpdateState state)
    {
        _wolfgateState = state;
        SupplyStatus.SetOwner(Entity);
    }

    private bool IsRecipeReady(LatheRecipePrototype recipe)
    {
        return _wolfgateState?.ReadyRecipes.Contains(recipe.ID) == true;
    }

    private int GetSiloPartCount(EntProtoId part)
    {
        return FabricationSilo.GetPartAmount(Entity, part);
    }

    private FixedPoint2 GetSiloReagentAmount(ProtoId<ReagentPrototype> reagent)
    {
        return FabricationSilo.GetReagentAmount(Entity, reagent);
    }

    /// <summary>
    /// Shows the queue as cards, reusing each batch's card so a total being typed survives updates.
    /// </summary>
    private void PopulateWolfgateQueue(List<LatheRecipeBatch> queue)
    {
        var live = new HashSet<int>();
        for (var i = 0; i < queue.Count; i++)
        {
            var batch = queue[i];
            live.Add(batch.Index);
            if (!_queueEntries.TryGetValue(batch.Index, out var entry))
            {
                var index = batch.Index;
                entry = new LatheQueueEntry(_lathe.GetRecipeName(batch.Recipe), GetRecipeDisplayControl(batch.Recipe));
                entry.OnAmountChanged += amount => OnRecipeAmountChanged?.Invoke(index, amount);
                entry.OnCancelled += () => OnRecipeCancelled?.Invoke(index);
                _queueEntries[index] = entry;
                QueueList.AddChild(entry);
            }

            entry.SetPositionInParent(i);

            var supplies = _wolfgateState?.QueueSupplies.GetValueOrDefault(batch.Index);
            var printing = _wolfgateState?.PrintingBatch == batch.Index;
            var ready = printing || supplies?.Ready == true;
            var status = printing ? "lathe-menu-status-printing"
                : ready ? "lathe-menu-status-ready"
                : supplies is { DesignAvailable: false } ? "lathe-menu-status-design-unavailable"
                : "lathe-menu-status-waiting";
            var missing = !ready && supplies != null ? GetMissingSuppliesText(supplies) : null;

            entry.Update(i + 1, batch.ItemsPrinted, batch.ItemsRequested, Loc.GetString(status),
                string.IsNullOrEmpty(missing) ? null : missing);
        }

        foreach (var (index, entry) in _queueEntries.Where(pair => !live.Contains(pair.Key)).ToList())
        {
            _queueEntries.Remove(index);
            entry.Orphan();
        }

        if (queue.Count == 0)
        {
            _queueEmptyLabel ??= new Label
            {
                Text = Loc.GetString("lathe-menu-queue-empty"),
                StyleClasses = { "LabelSubText" },
                HorizontalAlignment = HAlignment.Center,
            };

            if (_queueEmptyLabel.Parent == null)
                QueueList.AddChild(_queueEmptyLabel);
        }
        else
        {
            _queueEmptyLabel?.Orphan();
        }
    }

    private string GetMissingSuppliesText(LatheMissingSupplies missing)
    {
        var supplies = new List<string>();
        foreach (var (id, amount) in missing.Materials)
        {
            if (!_prototypeManager.TryIndex(id, out var proto))
                continue;

            var sheets = amount / (float) _materialStorage.GetSheetVolume(proto);
            supplies.Add(Loc.GetString("lathe-menu-queue-missing-material",
                ("amount", sheets),
                ("unit", Loc.GetString(proto.Unit)),
                ("material", Loc.GetString(proto.Name))));
        }

        foreach (var (id, amount) in missing.Entities)
        {
            if (!_prototypeManager.TryIndex(id, out var proto))
                continue;

            supplies.Add(Loc.GetString("lathe-menu-queue-missing-entity", ("amount", amount), ("material", proto.Name)));
        }

        foreach (var (id, amount) in missing.Reagents)
        {
            if (!_prototypeManager.TryIndex(id, out var proto))
                continue;

            supplies.Add(Loc.GetString("lathe-menu-queue-missing-reagent",
                ("amount", amount.Float()),
                ("material", proto.LocalizedName)));
        }

        return string.Join("\n", supplies.Select(supply => Loc.GetString("lathe-menu-queue-missing-line", ("supply", supply))));
    }
}
