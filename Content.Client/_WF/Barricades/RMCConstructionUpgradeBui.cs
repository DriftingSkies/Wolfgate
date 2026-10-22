using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Barricades;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Barricades;

/// <summary>Opens the upgrade radial menu at the barricade.</summary>
[UsedImplicitly]
public sealed class RMCConstructionUpgradeBui : BoundUserInterface
{
    [Dependency] private IClyde _displayManager = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;

    private RMCConstructionUpgradeMenu? _menu;

    public RMCConstructionUpgradeBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);

        _sprite = EntMan.System<SpriteSystem>();
        _transform = EntMan.System<TransformSystem>();
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<RMCConstructionUpgradeMenu>();

        if (EntMan.TryGetComponent(Owner, out RMCConstructionUpgradeTargetComponent? upgradeComp) &&
            upgradeComp.Upgrades is { } upgrades)
        {
            foreach (var upgradeId in upgrades)
            {
                if (!_prototypes.TryIndex(upgradeId, out var upgradeProto))
                    continue;

                var button = new RadialMenuTextureButton
                {
                    StyleClasses = { "RadialMenuButton" },
                    SetSize = new Vector2(64, 64),
                    ToolTip = upgradeProto.Name,
                };

                button.AddChild(new TextureRect
                {
                    VerticalAlignment = Control.VAlignment.Center,
                    HorizontalAlignment = Control.HAlignment.Center,
                    Texture = _sprite.GetPrototypeIcon(upgradeProto).GetFrame(RsiDirection.South, 0),
                    TextureScale = new Vector2(2f, 2f),
                });

                button.OnButtonDown += _ => SendPredictedMessage(new RMCConstructionUpgradeBuiMsg(upgradeId));
                _menu.Upgrades.AddChild(button);
            }
        }

        if (EntMan.Deleted(Owner))
            return;

        var pos = _eye.WorldToScreen(_transform.GetMapCoordinates(Owner).Position) / _displayManager.ScreenSize;
        _menu.OpenCenteredAt(pos);
    }
}
