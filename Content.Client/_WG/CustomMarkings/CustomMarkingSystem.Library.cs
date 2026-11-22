using Content.Shared._WG.CustomMarkings;

namespace Content.Client._WG.CustomMarkings;

// The local player's library of saved markings, kept by the server.
public sealed partial class CustomMarkingSystem
{
    private List<CustomMarkingEntry>? _library;
    private int _lastRequest;

    /// <summary>The local player's saved markings, or null until the server has sent them.</summary>
    public IReadOnlyList<CustomMarkingEntry>? Library => _library;

    public event Action? LibraryUpdated;

    /// <summary>Raised with the server's answer to a <see cref="Save"/>.</summary>
    public event Action<CustomMarkingSaveResultEvent>? SaveAnswered;

    private void InitializeLibrary()
    {
        SubscribeNetworkEvent<CustomMarkingLibraryEvent>(OnLibrary);
        SubscribeNetworkEvent<CustomMarkingSaveResultEvent>(OnSaveResult);
    }

    public void RequestLibrary()
    {
        RaiseNetworkEvent(new CustomMarkingLibraryRequestEvent());
    }

    /// <summary>
    /// Saves a marking to the library: a new one for id 0, offered to the character in <paramref name="slot"/> or
    /// to every character for null; otherwise a change to that entry, which keeps its own. Null art keeps the
    /// entry's own. Returns the request number its <see cref="SaveAnswered"/> will carry.
    /// </summary>
    public int Save(int id, string name, CustomMarkingPlacement placement, CustomMarkingArt? art, int? slot = null)
    {
        var request = ++_lastRequest;
        RaiseNetworkEvent(new CustomMarkingSaveEvent(
            request,
            id,
            name,
            placement,
            art?.Pixels,
            art?.GetFrameTimes(),
            art != null && art.HasErase() ? art.Erase : null,
            art?.PingPongs ?? false,
            art != null && art.HasGlow() ? art.Glow : null,
            slot));
        return request;
    }

    /// <summary>Offers an entry to the character in a slot only, or to every character for null.</summary>
    public void SetScope(int id, int? slot)
    {
        RaiseNetworkEvent(new CustomMarkingScopeEvent(id, slot));
    }

    public void Delete(int id)
    {
        RaiseNetworkEvent(new CustomMarkingDeleteEvent(id));
    }

    /// <summary>
    /// Offers the markings kept for a slot to every character, as the server does when that slot's character is
    /// deleted. The server sends no new library for that, so the one held here would go on naming the slot.
    /// </summary>
    public void ReleaseSlot(int slot)
    {
        if (_library == null)
            return;

        var released = false;
        for (var i = 0; i < _library.Count; i++)
        {
            if (_library[i].Slot != slot)
                continue;

            _library[i] = _library[i] with { Slot = null };
            released = true;
        }

        if (released)
            LibraryUpdated?.Invoke();
    }

    private void OnLibrary(CustomMarkingLibraryEvent ev)
    {
        _library = ev.Entries;
        LibraryUpdated?.Invoke();
    }

    private void OnSaveResult(CustomMarkingSaveResultEvent ev)
    {
        SaveAnswered?.Invoke(ev);
    }
}
