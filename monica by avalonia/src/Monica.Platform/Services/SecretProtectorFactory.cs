namespace Monica.Platform.Services;

public static class SecretProtectorFactory
{
    public static ISecretProtector Create(IPlatformIntegrationService platformIntegrationService) =>
        new LinuxSecretProtector(platformIntegrationService);
}
