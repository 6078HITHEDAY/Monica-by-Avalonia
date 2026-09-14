using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Monica.Platform.Services;

public interface ILinuxKeyringStore
{
    string? LookupPassword(string attributeKey, string attributeValue);
    void StorePassword(string label, string attributeKey, string attributeValue, string password);
}

public sealed class LinuxLibsecretKeyringStore : ILinuxKeyringStore
{
    private const string LibraryName = "libsecret-1.so.0";
    private const string SchemaName = "com.monicapass.desktop";
    private const string Collection = "default";
    private static readonly SecretSchema Schema = CreateSchema();

    public string? LookupPassword(string attributeKey, string attributeValue)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeValue);

        var error = IntPtr.Zero;
        var passwordPtr = IntPtr.Zero;
        var schema = Schema;
        try
        {
            passwordPtr = secret_password_lookup_sync(
                ref schema,
                IntPtr.Zero,
                out error,
                attributeKey,
                attributeValue,
                IntPtr.Zero);
            ThrowIfError(error, "Secret Service lookup failed.");
            return passwordPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(passwordPtr);
        }
        catch (DllNotFoundException exception)
        {
            throw new InvalidOperationException(
                "libsecret is unavailable. Install libsecret-1 and ensure a Secret Service provider is running.",
                exception);
        }
        finally
        {
            if (passwordPtr != IntPtr.Zero)
            {
                secret_password_free(passwordPtr);
            }

            FreeError(error);
        }
    }

    public void StorePassword(string label, string attributeKey, string attributeValue, string password)
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeValue);
        ArgumentNullException.ThrowIfNull(password);

        var error = IntPtr.Zero;
        var schema = Schema;
        try
        {
            var stored = secret_password_store_sync(
                ref schema,
                Collection,
                label,
                password,
                IntPtr.Zero,
                out error,
                attributeKey,
                attributeValue,
                IntPtr.Zero);
            ThrowIfError(error, "Secret Service store failed.");
            if (!stored)
            {
                throw new InvalidOperationException("Secret Service rejected the wrapping-key store request.");
            }
        }
        catch (DllNotFoundException exception)
        {
            throw new InvalidOperationException(
                "libsecret is unavailable. Install libsecret-1 and ensure a Secret Service provider is running.",
                exception);
        }
        finally
        {
            FreeError(error);
        }
    }

    private static SecretSchema CreateSchema()
    {
        var attributes = new SecretSchemaAttribute[32];
        attributes[0] = new SecretSchemaAttribute
        {
            Name = "key",
            Type = SecretSchemaAttributeType.String
        };
        return new SecretSchema
        {
            Name = SchemaName,
            Flags = 0,
            Attributes = attributes
        };
    }

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("libsecret is only available on Linux.");
        }
    }

    private static void ThrowIfError(IntPtr error, string fallbackMessage)
    {
        if (error == IntPtr.Zero)
        {
            return;
        }

        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, IntPtr.Size * 2))
            ?? fallbackMessage;
        throw new InvalidOperationException(message);
    }

    private static void FreeError(IntPtr error)
    {
        if (error != IntPtr.Zero)
        {
            g_error_free(error);
        }
    }

    private enum SecretSchemaAttributeType
    {
        String = 0
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchemaAttribute
    {
        public string? Name;
        public SecretSchemaAttributeType Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchema
    {
        public string Name;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public SecretSchemaAttribute[] Attributes;
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr secret_password_lookup_sync(
        ref SecretSchema schema,
        IntPtr cancellable,
        out IntPtr error,
        string attributeName,
        string attributeValue,
        IntPtr end);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool secret_password_store_sync(
        ref SecretSchema schema,
        string collection,
        string label,
        string password,
        IntPtr cancellable,
        out IntPtr error,
        string attributeName,
        string attributeValue,
        IntPtr end);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_error_free(IntPtr error);
}

public sealed class LinuxSecretProtector : ISecretProtector
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string WrappingKeyAttribute = "key";
    private const string WrappingKeyAttributeValue = "settings-wrapping-key";
    private const string WrappingKeyLabel = "Monica settings wrapping key";

    private readonly IPlatformIntegrationService _platformIntegrationService;
    private readonly ILinuxKeyringStore _keyringStore;
    private readonly object _keyGate = new();
    private byte[]? _wrappingKey;

    public LinuxSecretProtector(
        IPlatformIntegrationService platformIntegrationService,
        ILinuxKeyringStore? keyringStore = null)
    {
        _platformIntegrationService = platformIntegrationService;
        _keyringStore = keyringStore ?? new LinuxLibsecretKeyringStore();
    }

    public PlatformIntegrationCapability Capability =>
        _platformIntegrationService.GetCapability(PlatformFeatureKeys.SecretProtection);

    public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCapabilityAvailable();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        try
        {
            var key = GetOrCreateWrappingKey();
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var cipherBytes = new byte[plainBytes.Length];
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

            var payload = new byte[NonceSize + TagSize + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
            Buffer.BlockCopy(cipherBytes, 0, payload, NonceSize + TagSize, cipherBytes.Length);
            return Task.FromResult(Convert.ToBase64String(payload));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    public Task<string> UnprotectAsync(string protectedText, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCapabilityAvailable();
        var payload = Convert.FromBase64String(protectedText);
        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Invalid protected settings payload.");
        }

        var key = GetOrCreateWrappingKey();
        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var cipherBytes = payload[(NonceSize + TagSize)..];
        var plainBytes = new byte[cipherBytes.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
            return Task.FromResult(Encoding.UTF8.GetString(plainBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private byte[] GetOrCreateWrappingKey()
    {
        if (_wrappingKey is not null)
        {
            return _wrappingKey;
        }

        lock (_keyGate)
        {
            if (_wrappingKey is not null)
            {
                return _wrappingKey;
            }

            var existing = _keyringStore.LookupPassword(WrappingKeyAttribute, WrappingKeyAttributeValue);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                var decoded = Convert.FromBase64String(existing);
                if (decoded.Length != KeySize)
                {
                    throw new InvalidOperationException("Secret Service returned an invalid wrapping key.");
                }

                _wrappingKey = decoded;
                return _wrappingKey;
            }

            var created = RandomNumberGenerator.GetBytes(KeySize);
            _keyringStore.StorePassword(
                WrappingKeyLabel,
                WrappingKeyAttribute,
                WrappingKeyAttributeValue,
                Convert.ToBase64String(created));
            _wrappingKey = created;
            return _wrappingKey;
        }
    }

    private void EnsureCapabilityAvailable()
    {
        if (!Capability.IsUsable)
        {
            throw CreateUnsupportedException(
                Capability.UnsupportedReason ?? "Linux Secret Service protection is not available.");
        }
    }

    private static InvalidOperationException CreateUnsupportedException(string message) => new(message);
}
