using Content.Shared._WF.Outposts;

namespace Content.Client._WF.Outposts;

/// <summary>Holds the player's outpost saves as last sent, for the character editor's Outposts tab, and fetches previews.</summary>
public sealed class WFOutpostSavesSystem : EntitySystem
{
    /// <summary>The player's save slots, empty ones null; null until the server has sent them.</summary>
    public List<WFOutpostSaveSlot?>? Slots { get; private set; }

    /// <summary>Raised when the server sends the saves.</summary>
    public event Action? SavesUpdated;

    /// <summary>Raised when the server answers a preview request.</summary>
    public event Action<WFOutpostPreviewEvent>? PreviewReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFOutpostSavesEvent>(OnSaves);
        SubscribeNetworkEvent<WFOutpostPreviewEvent>(ev => PreviewReceived?.Invoke(ev));
    }

    private void OnSaves(WFOutpostSavesEvent ev)
    {
        Slots = ev.Slots;
        SavesUpdated?.Invoke();
    }

    /// <summary>Asks the server for the saves.</summary>
    public void RequestSaves()
    {
        RaiseNetworkEvent(new WFOutpostSavesRequestEvent());
    }

    /// <summary>Asks the server to empty a slot.</summary>
    public void RequestDelete(int slot)
    {
        RaiseNetworkEvent(new WFOutpostDeleteSaveRequestEvent(slot));
    }

    /// <summary>Asks the server for a save's grid, to preview it.</summary>
    public void RequestPreview(int slot)
    {
        RaiseNetworkEvent(new WFOutpostPreviewRequestEvent(slot));
    }
}
