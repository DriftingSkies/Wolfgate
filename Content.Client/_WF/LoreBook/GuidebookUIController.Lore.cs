using System.Linq;
using Content.Client._WF.LoreBook;
using Content.Shared.Guidebook;
using Robust.Shared.Prototypes;

namespace Content.Client.UserInterface.Systems.Guidebook;

public sealed partial class GuidebookUIController
{
    /// <summary>
    /// Takes lore pages out of a guidebook request, or out of the whole guidebook when none is given. If nothing else
    /// was asked for, opens the lore book instead and returns true. Lore children only hang under lore pages, so the
    /// children added later are never lore.
    /// </summary>
    private bool WFTakeOutLore(
        ref Dictionary<ProtoId<GuideEntryPrototype>, GuideEntry>? guides,
        ref List<ProtoId<GuideEntryPrototype>>? rootEntries,
        ref ProtoId<GuideEntryPrototype>? forceRoot,
        ref ProtoId<GuideEntryPrototype>? selected)
    {
        var lore = EntityManager.System<WFLoreBookSystem>();
        if (guides == null)
        {
            guides = _prototypeManager.EnumeratePrototypes<GuideEntryPrototype>()
                .Where(x => !lore.IsLore(x.ID))
                .ToDictionary(x => new ProtoId<GuideEntryPrototype>(x.ID), x => (GuideEntry) x);
            if (selected is { } wanted && lore.IsLore(wanted))
                selected = null;
            return false;
        }

        var loreIds = guides.Keys.Where(lore.IsLore).ToList();
        if (loreIds.Count == 0)
            return false;

        if (loreIds.Count == guides.Count)
        {
            UIManager.GetUIController<WFLoreBookUIController>().Open(selected ?? loreIds[0]);
            return true;
        }

        guides = new(guides);
        foreach (var id in loreIds)
            guides.Remove(id);

        rootEntries = rootEntries?.Where(id => !lore.IsLore(id)).ToList();
        if (forceRoot is { } root && lore.IsLore(root))
            forceRoot = null;
        if (selected is { } chosen && lore.IsLore(chosen))
            selected = null;

        return false;
    }
}
