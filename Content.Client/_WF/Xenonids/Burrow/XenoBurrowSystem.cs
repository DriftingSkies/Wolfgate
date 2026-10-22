using Content.Shared._WF.Xenonids.Burrow;
using Content.Shared._WF.Xenonids.Markers;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._WF.Xenonids.Burrow;

public sealed partial class XenoBurrowSystem : SharedXenoBurrowSystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        // WOLFGATE: RMC re-applied visibility every tick, which undid any other hiding of the sprite (the external
        // shuttle view hides every mob); apply it once per state change instead.
        SubscribeLocalEvent<XenoBurrowComponent, AfterAutoHandleStateEvent>(OnBurrowState);
        SubscribeLocalEvent<XenoBurrowComponent, LocalPlayerAttachedEvent>(OnLocalPlayerChanged);
        SubscribeLocalEvent<XenoBurrowComponent, LocalPlayerDetachedEvent>(OnLocalPlayerChanged);
    }

    private void OnBurrowState(Entity<XenoBurrowComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Apply(ent);
    }

    private void OnLocalPlayerChanged<T>(Entity<XenoBurrowComponent> ent, ref T args)
    {
        Apply(ent);
    }

    private void Apply(Entity<XenoBurrowComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        var burrowed = ent.Comp.Active;
        if (_player.LocalEntity == ent.Owner)
        {
            // taking over a xeno this system hid: the player sees themselves translucent, not not at all
            if (ent.Comp.HiddenByBurrow)
            {
                ent.Comp.HiddenByBurrow = false;
                _sprite.SetVisible((ent, sprite), true);
            }

            _sprite.SetColor((ent, sprite), burrowed ? Color.White.WithAlpha(0.4f) : Color.White);
        }
        else if (burrowed)
        {
            // only remember a hide of our own; something else may have hidden it already
            if (sprite.Visible)
            {
                _sprite.SetVisible((ent, sprite), false);
                ent.Comp.HiddenByBurrow = true;
            }
        }
        else if (ent.Comp.HiddenByBurrow)
        {
            ent.Comp.HiddenByBurrow = false;
            _sprite.SetVisible((ent, sprite), true);
        }

        if (TryComp(ent, out RMCNightVisionVisibleComponent? nightVision))
            nightVision.Transparency = burrowed ? 0.4f : null;
    }
}
