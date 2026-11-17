using Robust.Client.UserInterface;

namespace Content.Client._WG.Cockpit;

/// <summary>Holds an MFD page that is only offered while another optional part has something to show.</summary>
public sealed class WGCockpitOptionalHost : Control, IWGCockpitOptionalPage
{
    private readonly IWGCockpitOptionalPage _source;

    public WGCockpitOptionalHost(IWGCockpitOptionalPage source, Control content)
    {
        _source = source;
        AddChild(content);
    }

    public bool Available => _source.Available;
}
