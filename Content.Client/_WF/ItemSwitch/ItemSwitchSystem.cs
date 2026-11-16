using Content.Client.Items;
using Content.Shared._WF.ItemSwitch;
using Content.Shared._WF.ItemSwitch.Components;
using Robust.Client.GameObjects;

namespace Content.Client._WF.ItemSwitch;

/// <summary>Client side of item switching: shows the current mode and swaps the sprite's state.</summary>
public sealed class ItemSwitchSystem : SharedItemSwitchSystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.ItemStatus<ItemSwitchComponent>(ent => new ItemSwitchStatusControl(ent));
        SubscribeLocalEvent<ItemSwitchComponent, AfterAutoHandleStateEvent>(OnChanged);
    }

    private void OnChanged(Entity<ItemSwitchComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisuals(ent, ent.Comp.State);
    }

    protected override void UpdateVisuals(Entity<ItemSwitchComponent> ent, string key)
    {
        base.UpdateVisuals(ent, key);

        if (ent.Comp.States.TryGetValue(key, out var state) && state.Sprite != null)
            _sprite.LayerSetSprite(ent.Owner, 0, state.Sprite);
    }
}
