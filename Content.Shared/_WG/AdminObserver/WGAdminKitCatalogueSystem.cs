using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.AdminObserver;

/// <summary>Answers what is in the admin observer's kit catalogue (<see cref="WGAdminKitCategoryPrototype"/>).</summary>
public sealed partial class WGAdminKitCatalogueSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly HashSet<string> _entries = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        Rebuild();
    }

    /// <summary>Whether an entity prototype is a kit item.</summary>
    public bool Contains(string id)
    {
        return _entries.Contains(id);
    }

    /// <summary>The catalogue's groups in display order.</summary>
    public List<WGAdminKitCategoryPrototype> Categories()
    {
        return _prototypes.EnumeratePrototypes<WGAdminKitCategoryPrototype>()
            .OrderBy(c => c.Order)
            .ThenBy(c => c.ID)
            .ToList();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WGAdminKitCategoryPrototype>())
            Rebuild();
    }

    private void Rebuild()
    {
        _entries.Clear();
        foreach (var category in _prototypes.EnumeratePrototypes<WGAdminKitCategoryPrototype>())
        {
            foreach (var entry in category.Entries)
            {
                _entries.Add(entry.Id);
            }
        }
    }
}
