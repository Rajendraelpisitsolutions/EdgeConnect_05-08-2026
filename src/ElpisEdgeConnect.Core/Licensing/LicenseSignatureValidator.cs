// ============================================================================
// File: Licensing/LicenseSignatureValidator.cs
// Purpose: RSA-PSS / SHA-256 / 2048-bit (minimum) signature verification for
//          license files. Stateless aside from the cached RSA key.
// Reference: ARCHITECTURE_BLUEPRINT.md §7.2, docs/licensing/license-file-format.md
//
// LOCKED algorithm choices (B3):
//   - RSA-PSS
//   - SHA-256 hash
//   - MGF1 with SHA-256
//   - Salt length == hash length
//   - Minimum key size 2048 bits
// ============================================================================

using System;
using System.Security.Cryptography;
using System.Text;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Verifies (and, when given a private key, signs) license payloads using
/// the algorithm locked in B3.
/// </summary>
/// <remarks>
/// The validator owns its <see cref="RSA"/> instance and must be disposed
/// when no longer needed. <see cref="LicenseManager"/> holds exactly one
/// validator for the lifetime of the process.
/// </remarks>
public sealed class LicenseSignatureValidator : IDisposable
{
    /// <summary>The minimum acceptable RSA key size in bits.</summary>
    public const int MinimumKeySizeBits = 2048;

    private static readonly RSASignaturePadding Padding = RSASignaturePadding.Pss;
    private static readonly HashAlgorithmName Hash = HashAlgorithmName.SHA256;

    private readonly RSA _rsa;

    /// <summary>
    /// Create a validator from a PEM-encoded RSA public key (typically
    /// <see cref="EmbeddedPublicKey.Pem"/>).
    /// </summary>
    public LicenseSignatureValidator(string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(publicKeyPem);
        _rsa = RSA.Create();
        _rsa.ImportFromPem(publicKeyPem);
        if (_rsa.KeySize < MinimumKeySizeBits)
        {
            _rsa.Dispose();
            throw new InvalidOperationException(
                $"License public key is {_rsa.KeySize} bits; minimum is {MinimumKeySizeBits}.");
        }
    }

    /// <summary>
    /// Verify a base64-encoded signature against the canonical bytes of the
    /// license payload.
    /// </summary>
    /// <param name="canonicalPayload">Canonical-JSON bytes (no signature field).</param>
    /// <param name="base64Signature">Base64-encoded RSA-PSS signature.</param>
    public bool Verify(byte[] canonicalPayload, string base64Signature)
    {
        ArgumentNullException.ThrowIfNull(canonicalPayload);
        ArgumentNullException.ThrowIfNull(base64Signature);
        byte[] sig;
        try
        {
            sig = Convert.FromBase64String(base64Signature);
        }
        catch (FormatException)
        {
            return false;
        }
        return _rsa.VerifyData(canonicalPayload, sig, Hash, Padding);
    }

    /// <summary>
    /// Compute the SHA-256 fingerprint of the SubjectPublicKeyInfo bytes
    /// of the loaded key, as uppercase hex. Test code uses this to detect
    /// accidental key replacement.
    /// </summary>
    public string ComputeSpkiFingerprint()
    {
        var spki = _rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(spki);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sign canonical-JSON bytes with the supplied PRIVATE key. Used by
    /// LicenseGen and by tests; never called by Core in production. The
    /// caller passes the private-key PEM in; we never load it from disk.
    /// </summary>
    public static string SignWithPrivateKey(byte[] canonicalPayload, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(canonicalPayload);
        ArgumentNullException.ThrowIfNull(privateKeyPem);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        if (rsa.KeySize < MinimumKeySizeBits)
        {
            throw new InvalidOperationException(
                $"License private key is {rsa.KeySize} bits; minimum is {MinimumKeySizeBits}.");
        }
        var sig = rsa.SignData(canonicalPayload, Hash, Padding);
        return Convert.ToBase64String(sig);
    }

    /// <inheritdoc/>
    public void Dispose() => _rsa.Dispose();
}
