using System.Linq;
using System.Numerics;
using Content.Client._WG.Administration.UI.GhostOffer;
using Content.Shared._WG.Administration.GhostOffer;
using Content.Shared.Ghost;
using Robust.Shared.Player;

namespace Content.Client._WG.Administration.GhostOffer;

/// <summary>
/// Shows ghosts the sign-up prompt when an admin offers them a ghost role.
/// </summary>
public sealed class GhostOfferSystem : EntitySystem
{
    private readonly Dictionary<int, GhostOfferPromptWindow> _prompts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GhostOfferEvent>(OnOffered);
        SubscribeNetworkEvent<GhostOfferSignUpResultEvent>(OnSignUpResult);
        SubscribeNetworkEvent<GhostOfferClosedEvent>(OnClosed);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CloseAll();
    }

    private void OnOffered(GhostOfferEvent ev)
    {
        if (_prompts.ContainsKey(ev.OfferId))
            return;

        var prompt = new GhostOfferPromptWindow(ev);
        prompt.SignUpPressed += () => RaiseNetworkEvent(new GhostOfferSignUpEvent(ev.OfferId));
        prompt.FollowPressed += () => RaiseNetworkEvent(new GhostOfferFollowEvent(ev.OfferId));
        prompt.OnClose += () => _prompts.Remove(ev.OfferId);

        _prompts[ev.OfferId] = prompt;
        prompt.OpenCenteredAt(new Vector2(0.5f, 0.3f));
    }

    private void OnSignUpResult(GhostOfferSignUpResultEvent ev)
    {
        if (_prompts.TryGetValue(ev.OfferId, out var prompt))
            prompt.SetStatus(ev.Message, ev.IsError);
    }

    private void OnClosed(GhostOfferClosedEvent ev)
    {
        if (_prompts.Remove(ev.OfferId, out var prompt))
            prompt.Close();
    }

    /// <summary>
    /// A ghost that takes a body, by any route, can't sign up any more.
    /// </summary>
    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent ev)
    {
        if (!HasComp<GhostComponent>(ev.Entity))
            CloseAll();
    }

    private void CloseAll()
    {
        // Closing a prompt removes it from the dictionary.
        foreach (var prompt in _prompts.Values.ToList())
        {
            prompt.Close();
        }

        _prompts.Clear();
    }
}
