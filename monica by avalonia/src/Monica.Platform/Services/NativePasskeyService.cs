namespace Monica.Platform.Services;

public sealed record NativePasskeySupport(
    bool IsWebAuthnClientApiAvailable,
    uint WebAuthnApiVersion,
    bool CanActAsSystemCredentialProvider,
    string StatusReason)
{
    public static NativePasskeySupport Unavailable(string reason) =>
        new(false, 0, false, reason);
}
