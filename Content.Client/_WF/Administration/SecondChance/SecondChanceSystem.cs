using System.Linq;
using System.Numerics;
using Content.Client._WF.Administration.UI.SecondChance;
using Content.Shared._WF.Administration.SecondChance;

namespace Content.Client._WF.Administration.SecondChance;

/// <summary>
/// Shows the yes/no prompt when an admin offers the player a second chance in their body.
/// </summary>
public sealed class SecondChanceSystem : EntitySystem
{
    private readonly Dictionary<int, SecondChancePromptWindow> _prompts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<SecondChanceOfferEvent>(OnOffered);
        SubscribeNetworkEvent<SecondChanceClosedEvent>(OnClosed);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var prompt in _prompts.Values.ToList())
        {
            prompt.Close();
        }

        _prompts.Clear();
    }

    private void OnOffered(SecondChanceOfferEvent ev)
    {
        if (_prompts.ContainsKey(ev.OfferId))
            return;

        var prompt = new SecondChancePromptWindow(ev);
        prompt.Answered += accepted => RaiseNetworkEvent(new SecondChanceAnswerEvent(ev.OfferId, accepted));
        prompt.OnClose += () => _prompts.Remove(ev.OfferId);

        _prompts[ev.OfferId] = prompt;
        prompt.OpenCenteredAt(new Vector2(0.5f, 0.3f));
    }

    private void OnClosed(SecondChanceClosedEvent ev)
    {
        if (_prompts.Remove(ev.OfferId, out var prompt))
            prompt.Close();
    }
}
