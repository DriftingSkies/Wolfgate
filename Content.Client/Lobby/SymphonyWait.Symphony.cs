using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.Lobby;

/// <summary>
/// How long the lobby waits for a file the panel uploaded: it reaches each client through the uploaded-resources
/// channel, which can be slower than the message naming it. Half a second at first, easing to three, five minutes in all.
/// </summary>
public static class SymphonyWait
{
    public const int Attempts = 110;

    public static int DelayMs(int attempt) => attempt < 20 ? 500 : 3000;

    /// <summary>
    /// Whether a file can be asked of the resource cache yet. An uploaded one may still be on its way, and the cache
    /// remembers a file it could not find for the rest of the session, so it is only asked once the file is here.
    /// </summary>
    public static bool Arrived(IResourceManager resources, string path)
    {
        return !path.StartsWith("/Uploaded/") || resources.ContentFileExists(new ResPath(path));
    }
}
