using Content.Client.Damage;
using Content.Shared._WG.BloodColour;
using Robust.Client.GameObjects;

namespace Content.Client._WG.BloodColour;

/// <summary>Tints a mob's brute damage overlays with its profile's blood colour instead of its species'.</summary>
public sealed partial class WGBloodColourVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string Brute = "Brute";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGBloodColourComponent, ComponentStartup>((uid, _, _) => Refresh(uid));
        SubscribeLocalEvent<WGBloodColourComponent, AfterAutoHandleStateEvent>((uid, _, _) => Refresh(uid));
        SubscribeLocalEvent<WGBloodColourComponent, ComponentShutdown>((uid, _, _) => Refresh(uid, true));
        SubscribeLocalEvent<DamageVisualsComponent, ComponentStartup>((uid, _, _) => Refresh(uid));
    }

    /// <summary>Recolours the brute layers the damage visualizer made, back to the species' colour when cleared.</summary>
    private void Refresh(EntityUid uid, bool cleared = false)
    {
        if (!TryComp(uid, out DamageVisualsComponent? visuals) || !TryComp(uid, out SpriteComponent? sprite) ||
            visuals.DamageOverlayGroups?.GetValueOrDefault(Brute) is not { } group)
            return;

        Color colour;
        if (!cleared && TryComp(uid, out WGBloodColourComponent? own))
            colour = own.Color;
        else
            colour = group.Color is { } hex ? Color.FromHex(hex) : Color.White;

        SetColour((uid, sprite), $"DamageOverlay{Brute}", colour);
        foreach (var layer in visuals.TargetLayerMapKeys)
        {
            SetColour((uid, sprite), $"{layer}{Brute}", colour);
        }
    }

    private void SetColour(Entity<SpriteComponent> ent, string key, Color colour)
    {
        if (_sprite.LayerMapTryGet(ent.AsNullable(), key, out var index, false))
            _sprite.LayerSetColor(ent.AsNullable(), index, colour);
    }
}
