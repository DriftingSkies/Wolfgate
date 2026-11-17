using Content.Shared._WG.Xenonids.Invisibility;
using Robust.Client.GameObjects;

namespace Content.Client._WG.Xenonids.Invisibility;

public sealed partial class XenoInvisibilityVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private EntityQuery<XenoActiveInvisibleComponent> _activeInvisibleQuery;

    public override void Initialize()
    {
        _activeInvisibleQuery = GetEntityQuery<XenoActiveInvisibleComponent>();
    }

    public override void Update(float frameTime)
    {
        var invisible = EntityQueryEnumerator<XenoTurnInvisibleComponent, SpriteComponent>();
        while (invisible.MoveNext(out var uid, out var comp, out var sprite))
        {
            var opacity = _activeInvisibleQuery.HasComp(uid) ? comp.Opacity : 1;
            _sprite.SetColor((uid, sprite), Color.White.WithAlpha(opacity));
        }
    }
}
