# Native Passkey Boundary

## Linux Desktop Scope

Monica Desktop preserves WebAuthn/FIDO2 metadata imported through supported vault formats. Linux builds do not probe a platform WebAuthn client API and do not register Monica as a system credential provider.

Android Credential Provider behavior remains Android-specific. Desktop metadata support must not be presented as equivalent to Android provider registration.

## Unsupported Provider Scope

The native-passkey integration capability is reported as `Unsupported` on Linux. The capability-only service therefore reports:

- `IsWebAuthnClientApiAvailable = false`;
- `WebAuthnApiVersion = 0`;
- `CanActAsSystemCredentialProvider = false`;
- an explicit unsupported reason for the native-passkey integration.

## Security Boundary

No native WebAuthn DLL, DBus credential portal, or browser-process handshake is attempted for system passkey requests. No credential IDs, relying-party data, challenges, private key material, or vault state cross a native passkey boundary on Linux.
