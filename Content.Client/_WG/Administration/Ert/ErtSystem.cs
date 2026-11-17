using System.Numerics;
using Content.Client._WG.Administration.UI.Ert;
using Content.Shared._WG.Administration.Ert;

namespace Content.Client._WG.Administration.Ert;

/// <summary>
/// Sends ERT Builder requests, and shows ghosts the sign-up prompt when a team is called.
/// </summary>
public sealed class ErtSystem : EntitySystem
{
    public event Action<string, bool>? ResultReceived;

    private readonly Dictionary<int, ErtPromptWindow> _prompts = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ErtSpawnResultEvent>(ev => ResultReceived?.Invoke(ev.Message, ev.IsError));
        SubscribeNetworkEvent<ErtCalledEvent>(OnCalled);
        SubscribeNetworkEvent<ErtSignUpResultEvent>(OnSignUpResult);
        SubscribeNetworkEvent<ErtClosedEvent>(OnClosed);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        foreach (var prompt in _prompts.Values)
        {
            prompt.Close();
        }

        _prompts.Clear();
    }

    public void RequestSpawn(ErtConfig config)
    {
        RaiseNetworkEvent(new ErtSpawnRequestEvent(config));
    }

    private void OnCalled(ErtCalledEvent ev)
    {
        if (_prompts.ContainsKey(ev.TeamId))
            return;

        var prompt = new ErtPromptWindow(ev);
        prompt.SignUpPressed += leader => RaiseNetworkEvent(new ErtSignUpEvent(ev.TeamId, leader));
        prompt.OnClose += () => _prompts.Remove(ev.TeamId);

        _prompts[ev.TeamId] = prompt;
        prompt.OpenCenteredAt(new Vector2(0.5f, 0.3f));
    }

    private void OnSignUpResult(ErtSignUpResultEvent ev)
    {
        if (_prompts.TryGetValue(ev.TeamId, out var prompt))
            prompt.SetStatus(ev.Message, ev.IsError);
    }

    private void OnClosed(ErtClosedEvent ev)
    {
        if (_prompts.Remove(ev.TeamId, out var prompt))
            prompt.Close();
    }
}
