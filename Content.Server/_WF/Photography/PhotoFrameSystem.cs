using Content.Shared._WF.Photography;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._WF.Photography;

/// <summary>Wall frames: putting a photo in, cutting it out and showing it.</summary>
public sealed partial class PhotoFrameSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PhotoFrameComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PhotoFrameComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PhotoFrameComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PhotoFrameComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInit(Entity<PhotoFrameComponent> ent, ref ComponentInit args)
    {
        _container.EnsureContainer<ContainerSlot>(ent, PhotoFrameComponent.ContainerId);
    }

    private void OnMapInit(Entity<PhotoFrameComponent> ent, ref MapInitEvent args)
    {
        UpdateLabel(ent);
    }

    private void OnInteractUsing(Entity<PhotoFrameComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var slot = _container.EnsureContainer<ContainerSlot>(ent, PhotoFrameComponent.ContainerId);
        if (HasComp<PhotoComponent>(args.Used))
        {
            args.Handled = true;
            if (slot.ContainedEntity != null)
            {
                _popup.PopupEntity(Loc.GetString("wf-photo-frame-full"), ent, args.User);
                return;
            }

            if (!_hands.TryDrop(args.User, args.Used) || !_container.Insert(args.Used, slot))
                return;

            UpdateLabel(ent);
            return;
        }

        if (slot.ContainedEntity is not { } photo || !_tool.HasQuality(args.Used, ent.Comp.CutQuality))
            return;

        args.Handled = true;
        _container.Remove(photo, slot);
        _hands.PickupOrDrop(args.User, photo);
        _audio.PlayPvs(ent.Comp.CutSound, ent);
        _popup.PopupEntity(Loc.GetString("wf-photo-frame-cut", ("user", args.User), ("frame", ent.Owner)), ent);
        UpdateLabel(ent);
    }

    private void OnExamined(Entity<PhotoFrameComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var framed = _container.TryGetContainer(ent, PhotoFrameComponent.ContainerId, out var container)
            && container.ContainedEntities.Count > 0;
        if (framed)
            args.PushMarkup(Loc.GetString("wf-photo-frame-examine-photo"));
        else if (ent.Comp.PortraitState != null)
            args.PushMarkup(Loc.GetString("wf-photo-frame-examine-portrait"));
    }

    /// <summary>A portrait shows its own name and plaque until a photo covers it.</summary>
    private void UpdateLabel(Entity<PhotoFrameComponent> ent)
    {
        if (ent.Comp.PortraitName is not { } name || ent.Comp.PortraitDescription is not { } description)
            return;

        var framed = _container.TryGetContainer(ent, PhotoFrameComponent.ContainerId, out var container)
            && container.ContainedEntities.Count > 0;
        _meta.SetEntityName(ent, Loc.GetString(framed ? "wf-photo-frame-name" : name));
        _meta.SetEntityDescription(ent, Loc.GetString(framed ? "wf-photo-frame-description" : description));
    }
}
