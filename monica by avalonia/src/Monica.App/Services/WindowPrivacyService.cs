using Avalonia.Controls;

namespace Monica.App.Services;

public interface IWindowPrivacyService
{
    bool SetCaptureProtection(bool enabled);
}

public sealed class WindowPrivacyService(Func<Window?> windowProvider) : IWindowPrivacyService
{
    public bool SetCaptureProtection(bool enabled)
    {
        _ = windowProvider;
        _ = enabled;
        return false;
    }
}

internal sealed class DisabledWindowPrivacyService : IWindowPrivacyService
{
    public bool SetCaptureProtection(bool enabled) => false;
}
