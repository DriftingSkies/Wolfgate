using Content.Shared._WG.RMCCore.Sprite;
using Content.Shared._WG.Xenonids;
using Content.Shared._WG.Xenonids.Hide;
using Content.Shared._WG.Xenonids.Leap;
using Content.Shared._WG.Xenonids.Parasite;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Xenonids.Infected;

public sealed partial class XenoParasiteSystem : SharedXenoParasiteSystem
{
    [Dependency] private XenoVisualizerSystem _xenoVisualizer = default!;
    [Dependency] private TagSystem _tags = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<XenoParasiteComponent, GetDrawDepthEvent>(OnGetParasiteDrawDepth, before: [typeof(XenoHideSystem)]);
    }

    private void OnGetParasiteDrawDepth(Entity<XenoParasiteComponent> parasite, ref GetDrawDepthEvent args)
    {
        if (_tags.HasTag(parasite, ParasiteIsPreparingLeapProtoID) ||
            HasComp<XenoLeapingComponent>(parasite))
        {
            args.DrawDepth = Shared.DrawDepth.DrawDepth.Overdoors;
        }
        else
        {
            args.DrawDepth = Shared.DrawDepth.DrawDepth.Mobs;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<XenoComponent, ThrownItemComponent, SpriteComponent, AppearanceComponent>();
        while (query.MoveNext(out var uid, out _, out var thrown, out var sprite, out var appearance))
        {
            _xenoVisualizer.UpdateSprite((uid, sprite, null, appearance, null, thrown));
        }
    }
}
