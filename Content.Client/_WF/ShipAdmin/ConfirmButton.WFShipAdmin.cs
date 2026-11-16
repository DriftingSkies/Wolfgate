namespace Content.Client.UserInterface.Controls;

/// <summary>Lets the admin ship panel drop a confirmation that was asked for another ship.</summary>
public sealed partial class ConfirmButton
{
    /// <summary>Drops a pending confirmation and puts the label back, so the next press asks again.</summary>
    public void WfCancel()
    {
        if (!IsConfirming)
            return;

        IsConfirming = false;
        base.Text = Text;
        Disabled = false;
        DrawModeChanged();
    }
}
