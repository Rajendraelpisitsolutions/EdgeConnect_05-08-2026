// ============================================================================
// File: Licensing/LicenseSignatureValidatorTests.cs
// Covers: RSA-PSS / SHA-256 signature verification.
// ============================================================================

using System;
using System.Text;
using ElpisEdgeConnect.Core.Licensing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Licensing;

public sealed class LicenseSignatureValidatorTests
{
    [Fact]
    public void EmbeddedKey_FingerprintMatchesExpectedDevValue()
    {
        // Pin: catches accidental key swap. If the embedded key changes,
        // both EmbeddedPublicKey.Fingerprint AND this expected value must
        // be updated together — making the change impossible to do silently.
        using var v = new LicenseSignatureValidator(EmbeddedPublicKey.Pem);
        v.ComputeSpkiFingerprint().Should().Be(EmbeddedPublicKey.Fingerprint);
    }

    [Fact]
    public void Verify_ValidSignature_Succeeds()
    {
        using var v = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        var payload = Encoding.UTF8.GetBytes("hello world");
        var sig = LicenseSignatureValidator.SignWithPrivateKey(payload, TestRsaKeys.PrivatePem);

        v.Verify(payload, sig).Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedPayload_Fails()
    {
        using var v = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        var payload = Encoding.UTF8.GetBytes("hello world");
        var sig = LicenseSignatureValidator.SignWithPrivateKey(payload, TestRsaKeys.PrivatePem);

        var tampered = Encoding.UTF8.GetBytes("hello WORLD");
        v.Verify(tampered, sig).Should().BeFalse();
    }

    [Fact]
    public void Verify_GarbageSignature_Fails()
    {
        using var v = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        var payload = Encoding.UTF8.GetBytes("hello world");

        v.Verify(payload, "not-a-base64-signature!@#").Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongKey_Fails()
    {
        // Generate a separate keypair on the fly.
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var otherPrivate = rsa.ExportPkcs8PrivateKeyPem();

        using var v = new LicenseSignatureValidator(TestRsaKeys.PublicPem);
        var payload = Encoding.UTF8.GetBytes("hello world");
        var sig = LicenseSignatureValidator.SignWithPrivateKey(payload, otherPrivate);

        v.Verify(payload, sig).Should().BeFalse();
    }

    [Fact]
    public void Construct_ShortKey_Throws()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(1024);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();

        Action act = () => _ = new LicenseSignatureValidator(pem);
        act.Should().Throw<InvalidOperationException>();
    }
}
