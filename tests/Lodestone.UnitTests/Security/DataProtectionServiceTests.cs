using FluentAssertions;
using Lodestone.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Lodestone.UnitTests.Security;

public sealed class DataProtectionServiceTests
{
    [Fact]
    public void Protect_and_unprotect_round_trip_without_exposing_plaintext()
    {
        var service = CreateService();

        var protectedValue = service.Protect("A private reflection");

        service.IsProtected(protectedValue).Should().BeTrue();
        protectedValue.Should().NotContain("A private reflection");
        service.Unprotect(protectedValue).Should().Be("A private reflection");
    }

    [Fact]
    public void Unprotect_returns_legacy_plaintext_for_safe_backfill_compatibility()
    {
        var service = CreateService();

        service.Unprotect("Legacy plaintext").Should().Be("Legacy plaintext");
    }

    [Fact]
    public void Unprotect_rejects_a_tampered_protected_value()
    {
        var service = CreateService();
        var protectedValue = service.Protect("A private reflection");
        var tamperedValue = protectedValue[..^1] + (protectedValue[^1] == 'A' ? 'B' : 'A');

        var action = () => service.Unprotect(tamperedValue);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Sensitive protected data could not be decrypted*");
    }

    [Fact]
    public void Persisted_key_ring_survives_a_provider_restart_but_not_a_different_key_ring()
    {
        var keyDirectory = Path.Combine(Path.GetTempPath(), $"lodestone-keys-{Guid.NewGuid():N}");
        var wrongKeyDirectory = Path.Combine(Path.GetTempPath(), $"lodestone-wrong-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectory);
        Directory.CreateDirectory(wrongKeyDirectory);
        try
        {
            var first = new DataProtectionService(CreatePersistedProvider(keyDirectory));
            var protectedValue = first.Protect("A durable private reflection");

            var restarted = new DataProtectionService(CreatePersistedProvider(keyDirectory));
            restarted.Unprotect(protectedValue).Should().Be("A durable private reflection");

            var differentKeyRing = new DataProtectionService(CreatePersistedProvider(wrongKeyDirectory));
            var action = () => differentKeyRing.Unprotect(protectedValue);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("Sensitive protected data could not be decrypted*");
        }
        finally
        {
            if (Directory.Exists(keyDirectory)) Directory.Delete(keyDirectory, recursive: true);
            if (Directory.Exists(wrongKeyDirectory)) Directory.Delete(wrongKeyDirectory, recursive: true);
        }
    }

    private static DataProtectionService CreateService()
        => new(new EphemeralDataProtectionProvider());

    private static IDataProtectionProvider CreatePersistedProvider(string keyDirectory)
        => DataProtectionProvider.Create(
            new DirectoryInfo(keyDirectory),
            configuration => configuration.SetApplicationName("Lodestone.DataProtectionTests"));
}
