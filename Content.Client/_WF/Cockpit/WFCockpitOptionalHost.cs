using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>Holds an MFD page that is only offered while another optional part has something to show.</summary>
public sealed class WFCockpitOptionalHost : Control, IWFCockpitOptionalPage
{
    private readonly IWFCockpitOptionalPage _source;

    public WFCockpitOptionalHost(IWFCockpitOptionalPage source, Control content)
    {
        _source = source;
        AddChild(content);
    }

    public bool Available => _source.Available;
}
