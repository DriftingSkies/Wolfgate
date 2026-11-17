using Content.Shared._WG.RMCCore.Stealth;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.RMCCore.Stealth;

public sealed partial class EntityInvisibilityVisualsSystem : EntitySystem
{
    /// <summary>The one post-shader slot that auras and invisibility take turns on.</summary>
    public const string PostShaderId = "rmc-visibility";

    private static readonly ProtoId<ShaderPrototype> RMCInvisibleProto = "RMCInvisible";

    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EntityTurnInvisibleComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<EntityTurnInvisibleComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<EntityTurnInvisibleComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        _sprite.SetPostShader((ent, sprite), new SpriteComponent.PostShaderArgs
        {
            Id = PostShaderId,
            Shader = _prototypes.Index<ShaderPrototype>(RMCInvisibleProto).InstanceUnique(),
        });
    }

    private void OnShutdown(Entity<EntityTurnInvisibleComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        _sprite.RemovePostShader((ent, sprite), PostShaderId);
    }

    public override void Update(float frameTime)
    {
        var invisible = EntityQueryEnumerator<EntityTurnInvisibleComponent, SpriteComponent>();
        while (invisible.MoveNext(out var uid, out var comp, out var sprite))
        {
            var opacity =  TryComp<EntityActiveInvisibleComponent>(uid, out var activeInvisible) ? activeInvisible.Opacity : 1;
            if (_sprite.TryGetPostShader(sprite, PostShaderId, out var shader))
                shader.Shader.SetParameter("visibility", opacity);
        }
    }
}
