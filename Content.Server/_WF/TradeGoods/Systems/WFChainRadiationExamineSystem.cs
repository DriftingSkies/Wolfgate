using Content.Server._Mono.Radiation;
using Content.Shared.Examine;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>Warns on examine that chain-radiation cargo irradiates its neighbours and blows up when stacked.</summary>
public sealed class WFChainRadiationExamineSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ChainRadiationComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<ChainRadiationComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-trade-chain-radiation-examine"));
    }
}
