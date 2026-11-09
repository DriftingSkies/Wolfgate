using Content.Shared._WF.Photography;
using Content.Shared.Actions;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Photography;

/// <summary>Gives a station AI a camera built into its core, with a gallery and actions to use them.</summary>
public sealed partial class PhotoAiSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    private static readonly EntProtoId CaptureAction = "WFActionPhotoAiCapture";
    private static readonly EntProtoId GalleryAction = "WFActionPhotoGallery";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationAiHeldComponent, MapInitEvent>(OnHeldMapInit);
        SubscribeLocalEvent<StationAiHeldComponent, ComponentShutdown>(OnHeldShutdown);
    }

    private void OnHeldMapInit(Entity<StationAiHeldComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<PhotoCameraComponent>(ent))
        {
            var camera = AddComp<PhotoCameraComponent>(ent);
            camera.StoresPhotos = true;
            camera.CameraNetSight = true;
            camera.Flash = false;
            camera.Customise = false;
            camera.Silent = true;
            camera.Cooldown = TimeSpan.FromSeconds(2);
            Dirty(ent, camera);
        }

        EnsureComp<PhotoGalleryComponent>(ent);

        var actions = EnsureComp<PhotoAiActionsComponent>(ent);
        _actions.AddAction(ent, ref actions.Capture, CaptureAction);
        _actions.AddAction(ent, ref actions.Gallery, GalleryAction);
    }

    private void OnHeldShutdown(Entity<StationAiHeldComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<PhotoAiActionsComponent>(ent, out var actions))
            return;

        _actions.RemoveAction(ent, actions.Capture);
        _actions.RemoveAction(ent, actions.Gallery);
        actions.Capture = null;
        actions.Gallery = null;
    }
}

/// <summary>The photo actions given to a station AI while it sits in a core.</summary>
[RegisterComponent]
public sealed partial class PhotoAiActionsComponent : Component
{
    [DataField]
    public EntityUid? Capture;

    [DataField]
    public EntityUid? Gallery;
}
