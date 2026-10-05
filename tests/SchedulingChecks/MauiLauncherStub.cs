// The actual Razor approval component is tested without launching native apps.
namespace Microsoft.Maui.ApplicationModel;

public sealed class Launcher
{
    public static Launcher Default { get; } = new();
    public int Calls { get; private set; }
    public Task<bool> OpenAsync(string uri)
    {
        Calls++;
        return Task.FromResult(true);
    }
}
