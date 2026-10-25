namespace Content.Client.Lobby;

/// <summary>
/// How long the lobby waits for a file the panel uploaded: it reaches each client through the uploaded-resources
/// channel, which can be slower than the message naming it. Half a second at first, easing to three, five minutes in all.
/// </summary>
public static class SymphonyWait
{
    public const int Attempts = 110;

    public static int DelayMs(int attempt) => attempt < 20 ? 500 : 3000;
}
