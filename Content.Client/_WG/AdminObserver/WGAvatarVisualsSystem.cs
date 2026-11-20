using Content.Client.Clothing;
using Content.Client.Hands.Systems;
using Content.Client.Inventory;
using Content.Shared._WG.AdminObserver;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.AdminObserver;

/// <summary>
/// Draws a manifested observer as its avatar: an object or mob prototype's default sprite copied onto it, and its
/// own sprite put back when it hides. The copy is the prototype's look as written; it does not animate with state.
/// </summary>
public sealed partial class WGAvatarVisualsSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private ClientClothingSystem _clothing = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>Layers with this shader are displacement maps, which must keep it to work.</summary>
    private const string DisplacementShader = "DisplacedDraw";

    /// <summary>How often a character avatar's sprite is copied from its body again.</summary>
    private static readonly TimeSpan BodyRefresh = TimeSpan.FromSeconds(0.5);

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var query = EntityQueryEnumerator<WGManifestedComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var manifested, out var sprite))
        {
            var hologram = TryComp<WGStaffMarkerComponent>(uid, out var marker) && (marker.Visuals & WGMarkerVisuals.Hologram) != 0;
            var look = new WGAvatarLook(manifested.Kind, manifested.Prototype, manifested.Body, hologram);
            var visuals = EnsureComp<WGAvatarVisualsComponent>(uid);

            // A character's body can be dressed while it is shown, and may reach this client after the observer does.
            var stale = look.Kind == WGAvatarKind.Character
                        && (!visuals.Replaced || _timing.RealTime - visuals.LastCopy >= BodyRefresh);
            if (visuals.Look != look || stale)
            {
                visuals.Look = look;
                visuals.LastCopy = _timing.RealTime;
                Apply((uid, sprite), visuals, look);
            }

            // An avatar shows the avatar, not what the observer under it wears or holds.
            if (visuals.Replaced)
                ForgetWorn((uid, sprite), strip: true);
        }
    }

    /// <summary>Whether an entity prototype can be shown as an object or mob avatar: it can be spawned and has a sprite.</summary>
    public bool CanShow(string? id)
    {
        return id != null
               && _prototypes.TryIndex<EntityPrototype>(id, out var prototype)
               && !prototype.Abstract
               && prototype.TryComp<SpriteComponent>(out _, _factory);
    }

    /// <summary>Gives an observer that has stopped being manifested its own sprite back.</summary>
    public void Restore(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || !TryComp<WGAvatarVisualsComponent>(uid, out var visuals))
            return;

        if (visuals.Replaced && TryComp<SpriteComponent>(uid, out var sprite))
            CopyOwn((uid, sprite));

        RemComp<WGAvatarVisualsComponent>(uid);
    }

    private void Apply(Entity<SpriteComponent> ent, WGAvatarVisualsComponent visuals, WGAvatarLook look)
    {
        if (look.Kind is WGAvatarKind.Object or WGAvatarKind.Mob
            && _prototypes.TryIndex(look.Prototype, out var prototype)
            && prototype.TryComp<SpriteComponent>(out var source, _factory))
        {
            ForgetWorn(ent, strip: false);
            _sprite.CopySprite((EntityUid.Invalid, source), (ent.Owner, ent.Comp));
            visuals.Replaced = true;
        }
        else if (look.Kind == WGAvatarKind.Character && TryComp<SpriteComponent>(look.Body, out var body))
        {
            ForgetWorn(ent, strip: false);
            _sprite.CopySprite((look.Body.Value, body), (ent.Owner, ent.Comp));
            visuals.Replaced = true;
        }
        else if (visuals.Replaced)
        {
            CopyOwn(ent);
            visuals.Replaced = false;
        }

        if (look.Hologram)
            Unshade(ent);
    }

    /// <summary>Makes every layer glow in the dark, as a hologram does.</summary>
    private void Unshade(Entity<SpriteComponent> ent)
    {
        for (var i = 0; ent.Comp.TryGetLayer(i, out var layer); i++)
        {
            if (layer.ShaderPrototype != DisplacementShader)
                ent.Comp.LayerSetShader(i, "unshaded");
        }
    }

    /// <summary>Puts an entity's sprite back to its own prototype's, and lets its visualizers run again.</summary>
    private void CopyOwn(Entity<SpriteComponent> ent)
    {
        if (Prototype(ent) is not { } prototype || !prototype.TryComp<SpriteComponent>(out var own, _factory))
            return;

        ForgetWorn(ent, strip: false);
        _sprite.CopySprite((EntityUid.Invalid, own), (ent.Owner, ent.Comp));
        if (TryComp<AppearanceComponent>(ent, out var appearance))
            _appearance.QueueUpdate(ent, appearance);

        // Its own sprite shows what it wears and holds again.
        if (TryComp<InventoryComponent>(ent, out var inventory))
            _clothing.InitClothing(ent, inventory);

        foreach (var hand in _hands.EnumerateHands(ent))
        {
            if (hand.HeldEntity is { } held)
                RaiseLocalEvent(ent, new VisualsChangedEvent(GetNetEntity(held), hand.Name));
        }
    }

    /// <summary>
    /// The clothing and hands systems draw what an entity wears and holds as layers on its sprite, and remember
    /// each by key. Clears what they remember, and with <paramref name="strip"/> takes those layers off first.
    /// </summary>
    private void ForgetWorn(Entity<SpriteComponent> ent, bool strip)
    {
        if (TryComp<InventorySlotsComponent>(ent, out var slots))
        {
            foreach (var keys in slots.VisualLayerKeys.Values)
            {
                Forget(ent, keys, strip);
            }
        }

        if (TryComp<HandsComponent>(ent, out var hands))
        {
            foreach (var keys in hands.RevealedLayers.Values)
            {
                Forget(ent, keys, strip);
            }
        }
    }

    private static void Forget(Entity<SpriteComponent> ent, HashSet<string> keys, bool strip)
    {
        if (keys.Count == 0)
            return;

        if (strip)
        {
            foreach (var key in keys)
            {
                if (ent.Comp.LayerMapTryGet(key, out var index))
                    ent.Comp.RemoveLayer(index);
            }
        }

        keys.Clear();
    }
}
