using Content.Server._WG.Remade.Monolith;
using Content.Server.Cargo.Systems;
using Content.Server.Lathe.Components;
using Content.Server.Materials;
using Content.Shared._WG.Remade;
using Content.Shared.Construction;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Gifts;

/// <summary>
/// The Matter gift: slowly fills a material silo on the Scion's ship. Everything it makes is tracked as tainted: it can't be
/// ejected as sheets while it sits in the silo, and whatever a lathe prints from it sells for nothing.
/// </summary>
public sealed partial class WGMatterSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMaterialStorageSystem _storage = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WGMonolithSystem _monolith = default!;

    private static readonly TimeSpan Period = TimeSpan.FromSeconds(WGMonolithMath.MatterPeriod);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMatterStockComponent, ConsumeStoredMaterialsEvent>(OnConsume);
        SubscribeLocalEvent<WGMatterStockComponent, PriceCalculationEvent>(OnPrice);
        SubscribeLocalEvent<WGMatterStockComponent, DestructionEventArgs>(OnDestroyed, before: [typeof(MaterialStorageSystem)]);
        SubscribeLocalEvent<WGMatterStockComponent, MachineDeconstructedEvent>(OnDeconstructed, before: [typeof(MaterialStorageSystem)]);
        SubscribeLocalEvent<WGMatterEjectAttemptEvent>(OnEjectAttempt);
        SubscribeLocalEvent<LatheComponent, LatheStartPrintingEvent>(OnStartPrinting);
        SubscribeLocalEvent<WGMatterPrintComponent, WGLatheProducedEvent>(OnProduced);
        SubscribeLocalEvent<WGNoResaleComponent, PriceCalculationEvent>(OnNoResale);
        SubscribeLocalEvent<WGNoResaleComponent, ExaminedEvent>(OnNoResaleExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var stocks = EntityQueryEnumerator<WGMatterStockComponent>();
        while (stocks.MoveNext(out _, out var stock))
        {
            stock.Ejecting = false;
        }

        var prints = EntityQueryEnumerator<WGMatterPrintComponent>();
        while (prints.MoveNext(out var uid, out _))
        {
            if (!HasComp<LatheProducingComponent>(uid))
                RemCompDeferred<WGMatterPrintComponent>(uid);
        }

        var now = _timing.CurTime;
        var monoliths = EntityQueryEnumerator<WGMonolithComponent>();
        while (monoliths.MoveNext(out var uid, out var monolith))
        {
            if (now < monolith.NextMatter)
                continue;

            monolith.NextMatter = now + Period;
            Fill((uid, monolith));
        }
    }

    /// <summary>The material silo Matter fills: the nearest one on the Scion's grid.</summary>
    public EntityUid? FindSilo(EntityUid scion)
    {
        var xform = Transform(scion);
        if (xform.GridUid is not { } grid)
            return null;

        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var silos = EntityQueryEnumerator<OreSiloComponent, MaterialStorageComponent, TransformComponent>();
        while (silos.MoveNext(out var uid, out _, out _, out var siloXform))
        {
            if (siloXform.GridUid != grid)
                continue;

            var distance = (siloXform.LocalPosition - xform.LocalPosition).LengthSquared();
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = uid;
            }
        }

        return best;
    }

    private void Fill(Entity<WGMonolithComponent> ent)
    {
        var units = WGMonolithMath.MatterUnits(_monolith.GetDial(ent, WGGift.Matter), ent.Comp.Resonance);
        if (units <= 0 || FindSilo(ent) is not { } silo)
            return;

        var stock = EnsureComp<WGMatterStockComponent>(silo);
        foreach (var material in ent.Comp.MatterMaterials)
        {
            var tainted = stock.Tainted.GetValueOrDefault(material.Id);
            var add = Math.Min(units, WGMonolithMath.MatterCap - tainted);
            if (add <= 0 || !_storage.TryChangeMaterialAmount(silo, material.Id, add))
                continue;

            stock.Tainted[material.Id] = tainted + add;
        }
    }

    /// <summary>Tainted units of a material that are really in the silo.</summary>
    public int TaintedAmount(EntityUid silo, string material)
    {
        if (!TryComp<WGMatterStockComponent>(silo, out var stock) || !stock.Tainted.TryGetValue(material, out var tainted))
            return 0;

        return Math.Min(tainted, _storage.GetMaterialAmount(silo, material, localOnly: true));
    }

    private void OnConsume(Entity<WGMatterStockComponent> ent, ref ConsumeStoredMaterialsEvent args)
    {
        if (ent.Comp.Ejecting)
        {
            ent.Comp.Ejecting = false;
            return;
        }

        foreach (var (material, amount) in args.Materials)
        {
            if (amount >= 0 || !ent.Comp.Tainted.TryGetValue(material.Id, out var tainted))
                continue;

            var taken = Math.Min(tainted, -amount);
            if (taken >= tainted)
                ent.Comp.Tainted.Remove(material.Id);
            else
                ent.Comp.Tainted[material.Id] = tainted - taken;

            ent.Comp.Pending += taken;
        }
    }

    private void OnPrice(Entity<WGMatterStockComponent> ent, ref PriceCalculationEvent args)
    {
        foreach (var material in ent.Comp.Tainted.Keys)
        {
            if (_proto.TryIndex<MaterialPrototype>(material, out var proto))
                args.Price -= proto.Price * TaintedAmount(ent, material);
        }
    }

    private void OnDestroyed(Entity<WGMatterStockComponent> ent, ref DestructionEventArgs args)
    {
        Strip(ent);
    }

    private void OnDeconstructed(Entity<WGMatterStockComponent> ent, ref MachineDeconstructedEvent args)
    {
        Strip(ent);
    }

    /// <summary>Takes the Monolith's stock out of a silo that is about to drop its contents.</summary>
    private void Strip(Entity<WGMatterStockComponent> ent)
    {
        foreach (var material in ent.Comp.Tainted.Keys)
        {
            var amount = TaintedAmount(ent, material);
            if (amount > 0)
            {
                ent.Comp.Ejecting = true;
                _storage.TryChangeMaterialAmount(ent, material, -amount, localOnly: true);
            }
        }

        ent.Comp.Tainted.Clear();
        ent.Comp.Ejecting = false;
    }

    private void OnEjectAttempt(ref WGMatterEjectAttemptEvent args)
    {
        EntityUid? silo = HasComp<WGMatterStockComponent>(args.Storage) ? args.Storage : null;
        if (silo == null && TryComp<OreSiloClientComponent>(args.Storage, out var client) && HasComp<WGMatterStockComponent>(client.Silo))
            silo = client.Silo;

        if (silo is not { } found)
            return;

        var stored = _storage.GetMaterialAmount(found, args.Material, localOnly: true);
        var clean = stored - TaintedAmount(found, args.Material);
        if (args.Volume > clean)
        {
            args.Cancelled = true;
            _popup.PopupEntity(Loc.GetString("wg-matter-no-eject"), args.Storage, args.User);
            return;
        }

        // The sheets come out of the clean stock, so the draw they make leaves the tainted stock alone.
        Comp<WGMatterStockComponent>(found).Ejecting = true;
    }

    private void OnStartPrinting(Entity<LatheComponent> ent, ref LatheStartPrintingEvent args)
    {
        var pending = 0;
        if (TryComp<OreSiloClientComponent>(ent, out var client)
            && client.Silo is { } silo
            && TryComp<WGMatterStockComponent>(silo, out var stock))
        {
            pending = stock.Pending;
            stock.Pending = 0;
        }

        if (pending > 0)
            EnsureComp<WGMatterPrintComponent>(ent);
        else
            RemComp<WGMatterPrintComponent>(ent);
    }

    private void OnProduced(Entity<WGMatterPrintComponent> ent, ref WGLatheProducedEvent args)
    {
        EnsureComp<WGNoResaleComponent>(args.Product);
    }

    private void OnNoResale(Entity<WGNoResaleComponent> ent, ref PriceCalculationEvent args)
    {
        args.Price = 0;
        args.Handled = true;
    }

    private void OnNoResaleExamined(Entity<WGNoResaleComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-noresale-examine"));
    }
}
