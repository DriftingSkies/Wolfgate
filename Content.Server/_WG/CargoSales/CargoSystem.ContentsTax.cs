using Content.Shared._Mono.ItemTax.Components;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Containers;

namespace Content.Server.Cargo.Systems;

public sealed partial class CargoSystem
{
    /// <summary>
    /// Adds the item tax of taxed goods inside an untaxed thing on a pallet, a crate or bag of them, each on its own
    /// price. A taxed good's contents are already in its price and are not looked into.
    /// </summary>
    private void WgContentsTax(EntityUid holder, EntityUid grid, ref double blackMarket, ref double frontier, ref double nfsd, ref double medical)
    {
        if (!TryComp<ContainerManagerComponent>(holder, out var containers))
            return;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var good in container.ContainedEntities)
            {
                if (!TryComp<ItemTaxComponent>(good, out var tax))
                {
                    WgContentsTax(good, grid, ref blackMarket, ref frontier, ref nfsd, ref medical);
                    continue;
                }

                var price = _pricing.GetPriceWithVendingDiscount(good, grid);
                foreach (var (account, share) in tax.TaxAccounts)
                {
                    switch (account)
                    {
                        case SectorBankAccount.BlackMarket:
                            blackMarket += price * share;
                            break;
                        case SectorBankAccount.Frontier:
                            frontier += price * share;
                            break;
                        case SectorBankAccount.Nfsd:
                            nfsd += price * share;
                            break;
                        case SectorBankAccount.Medical:
                            medical += price * share;
                            break;
                    }
                }
            }
        }
    }
}
