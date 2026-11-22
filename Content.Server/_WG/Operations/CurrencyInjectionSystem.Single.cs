using Content.Server._Mono.Store.Components;
using Content.Shared._Mono.Company;
using Content.Shared.FixedPoint;
using Content.Shared.Store.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Store;

public sealed partial class CurrencyInjectionSystem
{
    /// <summary>
    /// Pays a faction once, into a single one of its uplinks: <paramref name="preferred"/> if it still takes the
    /// faction's money, otherwise its oldest uplink. <see cref="InjectCurrency(ProtoId{CompanyPrototype}, Dictionary{string, FixedPoint2})"/>
    /// pays every uplink in full, which a second uplink such as the Chengdu's would double.
    /// </summary>
    /// <returns>The uplink paid, or null when the faction has none.</returns>
    public EntityUid? InjectCurrencyOnce(ProtoId<CompanyPrototype> company, Dictionary<string, FixedPoint2> amount,
        EntityUid? preferred = null)
    {
        if (preferred is not { } target
            || TerminatingOrDeleted(target)
            || !TryComp<CurrencyInjectionTargetComponent>(target, out var targetComp)
            || targetComp.Company != company
            || !HasComp<StoreComponent>(target))
        {
            target = EntityUid.Invalid;
            var query = EntityQueryEnumerator<CurrencyInjectionTargetComponent, StoreComponent>();
            while (query.MoveNext(out var uid, out var comp, out _))
            {
                if (comp.Company == company && (!target.IsValid() || uid.Id < target.Id))
                    target = uid;
            }

            if (!target.IsValid())
                return null;
            targetComp = Comp<CurrencyInjectionTargetComponent>(target);
        }

        _store.TryAddCurrency(amount, target, Comp<StoreComponent>(target));
        foreach (var currency in amount)
            _radio.SendRadioMessage(target, Loc.GetString(targetComp.InjectionAlert, ("amount", currency.Value)), targetComp.RadioChannel, target);
        return target;
    }
}
