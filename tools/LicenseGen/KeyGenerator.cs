// ============================================================================
// File: KeyGenerator.cs
// Purpose: RSA-2048 keypair generator for license signing.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B3
// ============================================================================

using System.IO;
using System.Security.Cryptography;

namespace ElpisEdgeConnect.Tools.LicenseGen;

/// <summary>
/// Generates an RSA-2048 keypair and writes the public/private halves as
/// PEM files. The private key is intended for hand-off to the password
/// manager / HSM and is never persisted by the runtime.
/// </summary>
public static class KeyGenerator
{
    /// <summary>
    /// Create a fresh keypair under <paramref name="outputDirectory"/>.
    /// Writes <c>public.pem</c> and <c>private.pem</c>.
    /// </summary>
    public static void Generate(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var rsa = RSA.Create(2048);
        File.WriteAllText(Path.Combine(outputDirectory, "public.pem"), rsa.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(Path.Combine(outputDirectory, "private.pem"), rsa.ExportPkcs8PrivateKeyPem());
    }
}
