using Content.Client._WF.MarkingToggles.UI;
using Content.Shared._WF.CustomMarkings;
using Content.Shared._WF.MarkingToggles;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Verbs;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._WF.MarkingToggles;

/// <summary>Adds the Markings verb on the player's own body, which opens a window to show or hide each marking.</summary>
public sealed class MarkingToggleSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private MarkingToggleWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast: the verb goes on any body its player right-clicks, whatever components it has.
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (args.User != args.Target
            || args.User != _player.LocalEntity
            || !HasComp<HumanoidAppearanceComponent>(args.User))
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wf-marking-toggle-verb"),
            Message = Loc.GetString("wf-marking-toggle-verb-message"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png")),
            ClientExclusive = true,
            Act = () => OpenWindow(user),
        });
    }

    /// <summary>Opens the window for a body, or brings it to the front.</summary>
    public void OpenWindow(EntityUid body)
    {
        if (_window is { IsOpen: true })
        {
            _window.Body = body;
            _window.MoveToFront();
            return;
        }

        _window = _ui.CreateWindow<MarkingToggleWindow>();
        _window.Body = body;
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }

    public void SetVisible(MarkingCategories category, string markingId, bool visible)
    {
        RaiseNetworkEvent(new MarkingToggleRequestEvent(category, markingId, visible));
    }

    public void SetVisible(CustomMarking marking, bool visible)
    {
        RaiseNetworkEvent(new CustomMarkingToggleRequestEvent(marking, visible));
    }
}
