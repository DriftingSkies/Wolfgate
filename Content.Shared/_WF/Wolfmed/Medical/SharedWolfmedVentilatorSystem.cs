using Content.Shared.Body.Components;
using Content.Shared.DragDrop;

namespace Content.Shared._WF.Wolfmed.Medical;

/// <summary>
/// The half of the ventilator the client needs: dragging the stand onto a patient. The drag checks run on the client
/// before anything reaches the server, so they have to be answered here.
/// </summary>
public sealed class SharedWolfmedVentilatorSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedVentilatorComponent, CanDragEvent>(OnCanDrag);
        SubscribeLocalEvent<WolfmedVentilatorComponent, CanDropDraggedEvent>(OnCanDropDragged);
    }

    private void OnCanDrag(Entity<WolfmedVentilatorComponent> ent, ref CanDragEvent args)
    {
        args.Handled = true;
    }

    private void OnCanDropDragged(Entity<WolfmedVentilatorComponent> ent, ref CanDropDraggedEvent args)
    {
        if (!HasComp<BodyComponent>(args.Target))
            return;

        args.CanDrop = true;
        args.Handled = true;
    }
}
