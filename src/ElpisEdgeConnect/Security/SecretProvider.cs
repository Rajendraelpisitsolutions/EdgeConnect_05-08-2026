// ============================================================================
// File: Security/SecretProvider.cs
// Purpose: Resolves secrets (passwords, tokens) from multiple sources:
//          environment variables, AES-256 encrypted config values, or plain text.
//
// SECRET RESOLUTION FLOW:
//   1. Check if value starts with "env:" → read from environment variable
//   2. Check if value starts with "enc:" → decrypt with AES-256
//   3. Otherwise → treat as plain text (not recommended for production)
//
// AES ENCRYPTION SETUP:
//   Step 1: Generate a 256-bit AES key
//     $ dotnet run -- generate-key
//     → Output: "xK7mN3pQ9tB2wF5jH8kL1vR4uY6sA0eD3gI7nO2rT5="
//
//   Step 2: Set the key as an environment variable
//     $ export EDGECONNECT_ENCRYPTION_KEY="xK7mN3pQ9tB2wF5jH8kL1vR4uY6sA0eD3gI7nO2rT5="
//
//   Step 3: Encrypt a password
//     $ dotnet run -- encrypt "my-secret-password"
//     → Output: "enc:BASE64_IV:BASE64_CIPHERTEXT"
//
//   Step 4: Paste into appsettings.json
//     "Password": "enc:BASE64_IV:BASE64_CIPHERTEXT"
//
// PRODUCTION RECOMMENDATION:
//   Replace this with Azure Key Vault, HashiCorp Vault, or AWS Secrets Manager
//   for enterprise deployments. This implementation is suitable for small-to-medium
//   deployments where a dedicated secret management service isn't available.
// ============================================================================

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Security;

/// <summary>
/// Interface for secret resolution — allows easy mocking in unit tests
/// and swapping implementations (e.g., Azure Key Vault, HashiCorp Vault).
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Resolve a secret value from its stored format.
    /// 
    /// Supported formats:
    ///   "env:VAR_NAME"          → Read from environment variable VAR_NAME
    ///   "enc:BASE64IV:BASE64CT" → AES-256-CBC decrypt using EDGECONNECT_ENCRYPTION_KEY
    ///   "plaintext"             → Return as-is (not recommended for production)
    ///   "" or null              → Return empty string
    /// </summary>
    /// <param name="value">The stored secret value (may be encrypted or a reference)</param>
    /// <returns>The resolved plaintext secret</returns>
    string Resolve(string value);
}

/// <summary>
/// Default secret provider supporting environment variables and AES-256 encryption.
/// 
/// SECURITY NOTES:
///   - The AES encryption key is stored ONLY in an environment variable, never in config files
///   - AES-256-CBC with PKCS7 padding is used (industry standard)
///   - Each encrypted value has its own random IV (initialization vector)
///   - The IV is stored alongside the ciphertext (this is safe — IV doesn't need to be secret)
///   - Decrypted values are held in memory as managed strings (may appear in memory dumps)
/// </summary>
public sealed class SecretProvider : ISecretProvider
{
    private readonly ILogger<SecretProvider> _logger;

    /// <summary>
    /// The AES-256 encryption key loaded from the EDGECONNECT_ENCRYPTION_KEY env var.
    /// Null if the env var is not set (encrypted secrets will fail to resolve).
    /// </summary>
    private readonly byte[]? _encryptionKey;

    // ── Prefix constants for secret source detection ──
    // These prefixes tell the resolver how to interpret the value:
    private const string EnvPrefix = "env:";     // Environment variable reference
    private const string EncPrefix = "enc:";     // AES-encrypted value

    /// <summary>
    /// Initializes the SecretProvider and loads the AES encryption key
    /// from the EDGECONNECT_ENCRYPTION_KEY environment variable.
    /// 
    /// The key must be a base64-encoded 256-bit (32-byte) AES key.
    /// Generate one with: dotnet run -- generate-key
    /// </summary>
    public SecretProvider(ILogger<SecretProvider> logger)
    {
        _logger = logger;

        // Attempt to load the AES encryption key from environment
        var keyEnv = Environment.GetEnvironmentVariable("EDGECONNECT_ENCRYPTION_KEY");

        if (!string.IsNullOrEmpty(keyEnv))
        {
            try
            {
                // Decode the base64-encoded key into raw bytes
                _encryptionKey = Convert.FromBase64String(keyEnv);

                // Validate key length (must be exactly 256 bits = 32 bytes for AES-256)
                if (_encryptionKey.Length != 32)
                {
                    _logger.LogError(
                        "EDGECONNECT_ENCRYPTION_KEY is {Length} bytes, expected 32 bytes (256 bits)",
                        _encryptionKey.Length);
                    _encryptionKey = null; // Invalidate — wrong size
                }
                else
                {
                    _logger.LogInformation("AES-256 encryption key loaded from environment variable");
                }
            }
            catch (FormatException)
            {
                // The env var value isn't valid base64
                _logger.LogError(
                    "EDGECONNECT_ENCRYPTION_KEY is not valid base64 — encrypted secrets will fail");
                _encryptionKey = null;
            }
        }
        else
        {
            // Not an error — encryption key is optional if you don't use "enc:" values
            _logger.LogWarning(
                "EDGECONNECT_ENCRYPTION_KEY not set — encrypted secrets (enc:) will not be available. " +
                "Use 'dotnet run -- generate-key' to create one.");
        }
    }

    /// <summary>
    /// Resolves a secret value based on its prefix.
    /// 
    /// RESOLUTION ORDER:
    ///   1. Empty/null → return empty string (no-op)
    ///   2. "env:VAR_NAME" → read environment variable VAR_NAME
    ///   3. "enc:IV:CIPHERTEXT" → AES-256 decrypt
    ///   4. Anything else → return as-is (plain text)
    /// </summary>
    public string Resolve(string value)
    {
        // Guard: null or whitespace returns empty string (safe default)
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // ── Environment variable reference: "env:MY_SECRET_VAR" ──
        // This is the SIMPLEST and most Docker/Kubernetes-friendly approach.
        // Set the env var in your deployment config and reference it here.
        if (value.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Extract the variable name after "env:" prefix
            var envName = value[EnvPrefix.Length..]; // Equivalent to value.Substring(EnvPrefix.Length)

            // Read the environment variable value
            var envValue = Environment.GetEnvironmentVariable(envName);

            if (string.IsNullOrEmpty(envValue))
            {
                _logger.LogWarning(
                    "Environment variable '{EnvName}' referenced in config but not found — " +
                    "returning empty string. Did you forget to set it?",
                    envName);
                return string.Empty;
            }

            return envValue;
        }

        // ── AES-encrypted value: "enc:<base64-iv>:<base64-ciphertext>" ──
        // The value was encrypted using the 'encrypt' CLI command and the
        // EDGECONNECT_ENCRYPTION_KEY. Decrypt it now.
        if (value.StartsWith(EncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Strip the "enc:" prefix and pass the rest to the decryption method
            return DecryptAes(value[EncPrefix.Length..]);
        }

        // ── Plain text (fallback) ──
        // NOT RECOMMENDED for production — passwords appear in plain text
        // in config files, log outputs, and backups.
        return value;
    }

    /// <summary>
    /// Decrypts an AES-256-CBC encrypted payload.
    /// 
    /// Expected format: "BASE64_IV:BASE64_CIPHERTEXT"
    ///   - IV (Initialization Vector): 16 bytes, base64-encoded
    ///   - Ciphertext: Variable length, base64-encoded
    ///   - Separated by a colon ':'
    /// 
    /// The IV is stored alongside the ciphertext because:
    ///   1. The IV doesn't need to be secret (only the KEY must be secret)
    ///   2. Each encryption operation generates a RANDOM IV
    ///   3. Storing the IV with the ciphertext allows decryption without side-channel info
    /// </summary>
    /// <param name="encryptedPayload">Format: "BASE64_IV:BASE64_CIPHERTEXT"</param>
    /// <returns>Decrypted plaintext string, or empty string on failure</returns>
    private string DecryptAes(string encryptedPayload)
    {
        // Can't decrypt without the encryption key
        if (_encryptionKey is null)
        {
            _logger.LogError(
                "Cannot decrypt: EDGECONNECT_ENCRYPTION_KEY environment variable not set. " +
                "Run 'dotnet run -- generate-key' and set the key.");
            return string.Empty;
        }

        try
        {
            // Split into IV and ciphertext parts
            var parts = encryptedPayload.Split(':');
            if (parts.Length != 2)
                throw new FormatException(
                    "Expected format: '<base64-iv>:<base64-ciphertext>'. " +
                    $"Got {parts.Length} parts instead of 2.");

            // Decode IV (Initialization Vector) from base64
            // IV is always 16 bytes (128 bits) for AES regardless of key size
            var iv = Convert.FromBase64String(parts[0]);

            // Decode ciphertext from base64
            var ciphertext = Convert.FromBase64String(parts[1]);

            // Create AES decryptor with our stored key and the payload's IV
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;   // 256-bit key from environment
            aes.IV = iv;                // 128-bit IV from the encrypted payload
            aes.Mode = CipherMode.CBC;  // Cipher Block Chaining mode
            aes.Padding = PaddingMode.PKCS7; // Standard padding for block ciphers

            // Decrypt the ciphertext to plaintext bytes
            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

            // Convert decrypted bytes back to string
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            // Log the error but DON'T include the encrypted payload in the log
            // (it's not secret, but it's noisy and unhelpful)
            _logger.LogError(ex,
                "Failed to decrypt AES-encrypted secret — check that the encryption key " +
                "matches the one used to encrypt this value");
            return string.Empty;
        }
    }

    /// <summary>
    /// CLI UTILITY: Encrypt a plaintext secret for storing in appsettings.json.
    /// 
    /// Usage from command line:
    ///   $ dotnet run -- encrypt "my-secret-password"
    ///   → enc:BASE64_IV:BASE64_CIPHERTEXT
    /// 
    /// The output string can be pasted directly into the Password field
    /// of appsettings.json. At runtime, SecretProvider.Resolve() will
    /// decrypt it using the same AES key.
    /// 
    /// SECURITY: A new random IV is generated for EACH call, so encrypting
    /// the same password twice produces different ciphertext (this is correct
    /// and expected — it prevents pattern analysis).
    /// </summary>
    /// <param name="plaintext">The secret to encrypt</param>
    /// <param name="key">The 256-bit AES key (same as EDGECONNECT_ENCRYPTION_KEY)</param>
    /// <returns>Encrypted string in format "enc:BASE64_IV:BASE64_CIPHERTEXT"</returns>
    public static string Encrypt(string plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();  // Generate a RANDOM 16-byte IV for this encryption
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Format: "enc:BASE64_IV:BASE64_CIPHERTEXT"
        return $"enc:{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(cipherBytes)}";
    }

    /// <summary>
    /// CLI UTILITY: Generate a new random 256-bit AES encryption key.
    /// 
    /// Usage from command line:
    ///   $ dotnet run -- generate-key
    ///   → New AES-256 key: xK7mN3pQ9tB2wF5jH8kL1vR4uY6sA0eD3gI7nO2rT5=
    /// 
    /// The output should be stored in the EDGECONNECT_ENCRYPTION_KEY
    /// environment variable — NEVER in a config file or source code.
    /// </summary>
    /// <returns>Base64-encoded 256-bit AES key string</returns>
    public static string GenerateKey()
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;       // 256-bit = 32 bytes
        aes.GenerateKey();       // Cryptographically random key
        return Convert.ToBase64String(aes.Key);
    }
}
