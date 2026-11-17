namespace Content.Client._WG.Cockpit;

/// <summary>An MFD page whose selector only shows while the page has something to show.</summary>
public interface IWGCockpitOptionalPage
{
    bool Available { get; }
}
