using Content.Shared._WF.Photography;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;

namespace Content.Client._WF.Photography;

/// <summary>Draws a photo's thumbnail on photo and frame sprites, and the aged and portrait states.</summary>
public sealed partial class PhotoVisualizerSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private PhotoImageSystem _images = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string PhotoState = "photo";
    private const string OldPhotoState = "photo-old";
    private const string FrameState = "overlay";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoComponent, ComponentStartup>((uid, comp, _) => UpdatePhoto((uid, comp)));
        SubscribeLocalEvent<PhotoComponent, AfterAutoHandleStateEvent>((uid, comp, _) => UpdatePhoto((uid, comp)));
        SubscribeLocalEvent<PhotoFrameComponent, ComponentStartup>((uid, comp, _) => UpdateFrame((uid, comp)));
        SubscribeLocalEvent<PhotoFrameComponent, AfterAutoHandleStateEvent>((uid, comp, _) => UpdateFrame((uid, comp)));
        SubscribeLocalEvent<PhotoFrameComponent, EntInsertedIntoContainerMessage>((uid, comp, _) =>
            UpdateFrame((uid, comp)));
        SubscribeLocalEvent<PhotoFrameComponent, EntRemovedFromContainerMessage>((uid, comp, _) =>
            UpdateFrame((uid, comp)));
    }

    private void UpdatePhoto(Entity<PhotoComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        Entity<SpriteComponent?> spriteEnt = (ent.Owner, sprite);
        if (_sprite.LayerMapTryGet(spriteEnt, PhotoVisualLayers.Base, out _, false))
            _sprite.LayerSetRsiState(spriteEnt, PhotoVisualLayers.Base, ent.Comp.Old ? OldPhotoState : PhotoState);

        SetThumbnail(spriteEnt, ent.Comp);

        if (_container.TryGetContainingContainer((ent.Owner, null, null), out var container)
            && TryComp<PhotoFrameComponent>(container.Owner, out var frame))
            UpdateFrame((container.Owner, frame));
    }

    private void UpdateFrame(Entity<PhotoFrameComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        Entity<SpriteComponent?> spriteEnt = (ent.Owner, sprite);
        PhotoComponent? photo = null;
        if (_container.TryGetContainer(ent, PhotoFrameComponent.ContainerId, out var container)
            && container.ContainedEntities.Count > 0)
        {
            TryComp(container.ContainedEntities[0], out photo);
        }

        if (_sprite.LayerMapTryGet(spriteEnt, PhotoVisualLayers.Base, out _, false))
        {
            var state = photo == null && ent.Comp.PortraitState is { } portrait ? portrait : FrameState;
            _sprite.LayerSetRsiState(spriteEnt, PhotoVisualLayers.Base, state);
        }

        if (_sprite.LayerMapTryGet(spriteEnt, PhotoVisualLayers.Photo, out _, false))
        {
            _sprite.LayerSetVisible(spriteEnt, PhotoVisualLayers.Photo, photo != null);
            if (photo != null)
                _sprite.LayerSetRsiState(spriteEnt, PhotoVisualLayers.Photo, photo.Old ? OldPhotoState : PhotoState);
        }

        SetThumbnail(spriteEnt, photo);
    }

    private void SetThumbnail(Entity<SpriteComponent?> sprite, PhotoComponent? photo)
    {
        if (!_sprite.LayerMapTryGet(sprite, PhotoVisualLayers.Thumbnail, out _, false))
            return;

        var texture = photo == null ? null : _images.Thumbnail(photo);
        _sprite.LayerSetVisible(sprite, PhotoVisualLayers.Thumbnail, texture != null);
        if (texture != null)
            _sprite.LayerSetTexture(sprite, PhotoVisualLayers.Thumbnail, texture);
    }
}
