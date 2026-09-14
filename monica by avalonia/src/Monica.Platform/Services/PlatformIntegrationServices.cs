using System.Diagnostics;
using Monica.Core.Models;

namespace Monica.Platform.Services;

public static class PlatformFeatureKeys
{
    public const string FilePicker = "file-picker";
    public const string SecretProtection = "secret-protection";
    public const string Tray = "tray";
    public const string GlobalHotkey = "global-hotkey";
    public const string BrowserBridge = "browser-bridge";
    public const string NativePasskey = "native-passkey";
    public const string NativeNotification = "native-notification";
    public const string WindowSecurity = "window-security";
    public const string ExternalLinks = "external-links";
}

public sealed record PlatformIntegrationCapability(
    string Key,
    PlatformFeatureStatus Status,
    string Description,
    string? UnsupportedReason = null)
{
    public bool IsUsable => Status is PlatformFeatureStatus.Available or PlatformFeatureStatus.DesktopEquivalent;
}

public interface IPlatformIntegrationService
{
    string PlatformName { get; }
    IReadOnlyList<PlatformIntegrationCapability> GetCapabilities();
    PlatformIntegrationCapability GetCapability(string key);
}

public sealed record PlatformFilePickerFileType(string Name, IReadOnlyList<string> Patterns);

public sealed record PickedTextFile(string FileName, string Content);
public sealed record PickedBinaryFile(string FileName, byte[] Content);

public interface ISecretProtector
{
    PlatformIntegrationCapability Capability { get; }
    Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default);
    Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default);
}

public interface IFileSystemPickerService
{
    PlatformIntegrationCapability Capability { get; }
    Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default);
    Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default);
    Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default);
    Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default);
}

public interface IBrowserBridgeService : IDisposable
{
    PlatformIntegrationCapability Capability { get; }
    bool IsRunning { get; }
    int Port { get; }
    string SessionToken { get; }
    string LastError { get; }
    bool TryStart(int port, Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials);
    void Stop();
}

public sealed record BrowserBridgeCredential(long Id, string Title, string Username, string Password, string Website);

public interface INativePasskeyService
{
    PlatformIntegrationCapability Capability { get; }
    NativePasskeySupport Support { get; }
}

public interface ITrayService : IDisposable
{
    PlatformIntegrationCapability Capability { get; }
    bool IsVisible { get; }
    void Initialize(Action showWindow, Action lockVault, Action exitApplication);
    void SetVisible(bool isVisible);
}

public interface IGlobalHotkeyService : IDisposable
{
    PlatformIntegrationCapability Capability { get; }
    bool IsRegistered { get; }
    string RegisteredGesture { get; }
    string LastError { get; }
    bool TryRegister(string gesture, Action activated);
    void Unregister();
}

public interface IExternalLinkService
{
    PlatformIntegrationCapability Capability { get; }
    Task OpenAsync(Uri uri, CancellationToken cancellationToken = default);
}

public sealed class PlatformIntegrationService : IPlatformIntegrationService
{
    private readonly IReadOnlyDictionary<string, PlatformIntegrationCapability> _capabilities;

    public PlatformIntegrationService()
        : this(DetectPlatformName(), DetectCapabilities())
    {
    }

    public PlatformIntegrationService(string platformName, IEnumerable<PlatformIntegrationCapability> capabilities)
    {
        PlatformName = platformName;
        _capabilities = capabilities.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
    }

    public string PlatformName { get; }

    public IReadOnlyList<PlatformIntegrationCapability> GetCapabilities() =>
        _capabilities.Values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray();

    public PlatformIntegrationCapability GetCapability(string key) =>
        _capabilities.TryGetValue(key, out var capability)
            ? capability
            : Unsupported(key, "This platform adapter has not declared this feature.");

    private static string DetectPlatformName() => "Linux";

    private static IReadOnlyList<PlatformIntegrationCapability> DetectCapabilities() =>
    [
        DesktopEquivalent(PlatformFeatureKeys.FilePicker, "Linux file picking is available through Avalonia storage APIs."),
        Available(PlatformFeatureKeys.SecretProtection, "Linux secret protection uses a Secret Service wrapping key."),
        Available(PlatformFeatureKeys.Tray, "Linux tray integration is available when the desktop provides StatusNotifier/AppIndicator support."),
        PlatformLimited(PlatformFeatureKeys.GlobalHotkey, "Global hotkeys depend on the compositor and desktop environment."),
        Available(PlatformFeatureKeys.BrowserBridge, "An authenticated loopback browser bridge is available for Linux desktop builds."),
        Available(PlatformFeatureKeys.ExternalLinks, "External links can be opened through the Linux desktop shell."),
        Unsupported(PlatformFeatureKeys.NativePasskey, "Android Credential Provider behavior is not available on Linux."),
        DesktopEquivalent(PlatformFeatureKeys.NativeNotification, "Desktop notifications can replace Android notification features."),
        PlatformLimited(PlatformFeatureKeys.WindowSecurity, "Linux screenshot/window privacy support depends on the compositor.")
    ];

    public static PlatformIntegrationCapability Available(string key, string description) =>
        new(key, PlatformFeatureStatus.Available, description);

    public static PlatformIntegrationCapability DesktopEquivalent(string key, string description) =>
        new(key, PlatformFeatureStatus.DesktopEquivalent, description);

    public static PlatformIntegrationCapability PlatformLimited(string key, string description) =>
        new(key, PlatformFeatureStatus.PlatformLimited, description, description);

    public static PlatformIntegrationCapability Unsupported(string key, string reason) =>
        new(key, PlatformFeatureStatus.Unsupported, reason, reason);
}

public sealed class UnsupportedSecretProtector(IPlatformIntegrationService platformIntegrationService) : ISecretProtector
{
    public PlatformIntegrationCapability Capability =>
        platformIntegrationService.GetCapability(PlatformFeatureKeys.SecretProtection);

    public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    private InvalidOperationException CreateUnsupportedException() =>
        new(Capability.UnsupportedReason ?? "Secret protection is not supported on this platform.");
}

public sealed class CapabilityOnlyFileSystemPickerService(IPlatformIntegrationService platformIntegrationService) : IFileSystemPickerService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.FilePicker);

    public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
        throw CreateUnsupportedException();

    private InvalidOperationException CreateUnsupportedException() =>
        new(Capability.UnsupportedReason ?? "File picking is not supported on this platform.");
}


public sealed class CapabilityOnlyNativePasskeyService(IPlatformIntegrationService platformIntegrationService) : INativePasskeyService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.NativePasskey);
    public NativePasskeySupport Support => NativePasskeySupport.Unavailable(Capability.UnsupportedReason ?? "Native passkey integration is unavailable on this platform.");
}


public sealed class CapabilityOnlyGlobalHotkeyService(IPlatformIntegrationService platformIntegrationService) : IGlobalHotkeyService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.GlobalHotkey);
    public bool IsRegistered => false;
    public string RegisteredGesture => "";
    public string LastError => Capability.UnsupportedReason ?? "Global hotkeys are unavailable.";
    public bool TryRegister(string gesture, Action activated) => false;
    public void Unregister() { }
    public void Dispose() { }
}

public sealed class SystemExternalLinkService(IPlatformIntegrationService platformIntegrationService) : IExternalLinkService
{
    public PlatformIntegrationCapability Capability => platformIntegrationService.GetCapability(PlatformFeatureKeys.ExternalLinks);

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Capability.IsUsable)
        {
            throw new InvalidOperationException(Capability.UnsupportedReason ?? "External links are not supported on this platform.");
        }

        var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });

        return process is null
            ? Task.FromException(new InvalidOperationException("The desktop shell did not accept the external link."))
            : Task.CompletedTask;
    }
}
