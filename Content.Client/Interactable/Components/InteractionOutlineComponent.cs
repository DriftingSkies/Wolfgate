using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Interactable.Components
{
    [RegisterComponent]
    public sealed partial class InteractionOutlineComponent : Component
    {
        [Dependency] private IPrototypeManager _prototypeManager = default!;
        [Dependency] private IEntityManager _entMan = default!;

        private const float DefaultWidth = 1;

        private static readonly ProtoId<ShaderPrototype> ShaderInRange = "SelectionOutlineInrange";

        private static readonly ProtoId<ShaderPrototype> ShaderOutOfRange = "SelectionOutline";

        private bool _inRange;
        private ShaderInstance? _shader;
        private int _lastRenderScale;

        // WOLFGATE(AdminObserver) START: the outline is its own post-shader pass, removed by id
        // The legacy PostShader property reads null once a sprite has two passes, and setting it wipes them. A manifested
        // admin's hologram and aura passes, re-added every frame, left the outline stranded on the sprite after its
        // shader was disposed, and drawing a disposed shader blacked out the screen.
        private const string OutlinePass = "interaction-outline";

        public void OnMouseEnter(EntityUid uid, bool inInteractionRange, int renderScale)
        {
            _lastRenderScale = renderScale;
            _inRange = inInteractionRange;
            var sprites = _entMan.System<SpriteSystem>();
            if (_entMan.TryGetComponent(uid, out SpriteComponent? sprite) && sprites.GetPostShaders(sprite).Count == 0)
            {
                _shader?.Dispose();
                _shader = MakeNewShader(inInteractionRange, renderScale);
                sprites.SetPostShader((uid, sprite), new SpriteComponent.PostShaderArgs(OutlinePass, _shader));
            }
        }

        public void OnMouseLeave(EntityUid uid)
        {
            if (_entMan.TryGetComponent(uid, out SpriteComponent? sprite))
            {
                _entMan.System<SpriteSystem>().RemovePostShader((uid, sprite), OutlinePass);
                sprite.RenderOrder = 0;
            }

            _shader?.Dispose();
            _shader = null;
        }

        public void UpdateInRange(EntityUid uid, bool inInteractionRange, int renderScale)
        {
            var sprites = _entMan.System<SpriteSystem>();
            if (_entMan.TryGetComponent(uid, out SpriteComponent? sprite)
                && sprites.HasPostShader((uid, sprite), OutlinePass)
                && (inInteractionRange != _inRange || _lastRenderScale != renderScale))
            {
                _inRange = inInteractionRange;
                _lastRenderScale = renderScale;

                var old = _shader;
                _shader = MakeNewShader(_inRange, _lastRenderScale);
                sprites.SetPostShader((uid, sprite), new SpriteComponent.PostShaderArgs(OutlinePass, _shader));
                old?.Dispose();
            }
        }

        // public void OnMouseEnter(EntityUid uid, bool inInteractionRange, int renderScale)
        // {
        //     _lastRenderScale = renderScale;
        //     _inRange = inInteractionRange;
        //     if (_entMan.TryGetComponent(uid, out SpriteComponent? sprite) && sprite.PostShader == null)
        //     {
        //         // TODO why is this creating a new instance of the outline shader every time the mouse enters???
        //         _shader = MakeNewShader(inInteractionRange, renderScale);
        //         sprite.PostShader = _shader;
        //     }
        // }
        //
        // public void OnMouseLeave(EntityUid uid)
        // {
        //     if (_entMan.TryGetComponent(uid, out SpriteComponent? sprite))
        //     {
        //         if (sprite.PostShader == _shader)
        //             sprite.PostShader = null;
        //         sprite.RenderOrder = 0;
        //     }
        //
        //     _shader?.Dispose();
        //     _shader = null;
        // }
        //
        // public void UpdateInRange(EntityUid uid, bool inInteractionRange, int renderScale)
        // {
        //     if (_entMan.TryGetComponent(uid, out SpriteComponent? sprite)
        //         && sprite.PostShader == _shader
        //         && (inInteractionRange != _inRange || _lastRenderScale != renderScale))
        //     {
        //         _inRange = inInteractionRange;
        //         _lastRenderScale = renderScale;
        //
        //         _shader = MakeNewShader(_inRange, _lastRenderScale);
        //         sprite.PostShader = _shader;
        //     }
        // }
        // WOLFGATE END

        private ShaderInstance MakeNewShader(bool inRange, int renderScale)
        {
            var shaderName = inRange ? ShaderInRange : ShaderOutOfRange;

            var instance = _prototypeManager.Index<ShaderPrototype>(shaderName).InstanceUnique();
            instance.SetParameter("outline_width", DefaultWidth * renderScale);
            return instance;
        }
    }
}
