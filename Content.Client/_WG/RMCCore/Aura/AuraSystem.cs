using Content.Shared._WG.RMCCore.Aura;
using Content.Shared._WG.RMCCore.Stealth;
using Robust.Client.GameObjects;
using Content.Client._WG.RMCCore.Stealth;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.RMCCore.Aura;

public sealed partial class AuraSystem : SharedAuraSystem
{
    private static readonly ProtoId<ShaderPrototype> RMCAuraOutlineProto = "RMCAuraOutline";

    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AuraComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AuraComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<AuraComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        _sprite.SetPostShader((ent, sprite), new SpriteComponent.PostShaderArgs
        {
            Id = EntityInvisibilityVisualsSystem.PostShaderId,
            Shader = _prototypes.Index<ShaderPrototype>(RMCAuraOutlineProto).InstanceUnique(),
        });
    }

    private void OnShutdown(Entity<AuraComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        if (HasComp<EntityActiveInvisibleComponent>(ent))
            return;

        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        _sprite.RemovePostShader((ent, sprite), EntityInvisibilityVisualsSystem.PostShaderId);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var auraQuery = EntityQueryEnumerator<AuraComponent, SpriteComponent>();

        while (auraQuery.MoveNext(out var uid, out var aura, out var sprite))
        {
            if (!_sprite.TryGetPostShader(sprite, EntityInvisibilityVisualsSystem.PostShaderId, out var shader))
                continue;

            shader.Shader.SetParameter("outline_color", aura.Color);
            shader.Shader.SetParameter("outline_width", aura.OutlineWidth);
        }
    }
}
