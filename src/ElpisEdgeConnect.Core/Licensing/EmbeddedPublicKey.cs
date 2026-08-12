// ============================================================================
// File: Licensing/EmbeddedPublicKey.cs
//
// !!! DEV KEY - REPLACE BEFORE PRODUCTION !!!
//
// Purpose: RSA public key compiled into the binary so license verification
//          works fully offline. The matching private key is held externally
//          and is NEVER checked into the repository.
//
// CURRENT STATE: This is the DEVELOPMENT public key. Its private counterpart
// is committed under tests/ for reproducible test signing. Any binary built
// from this source tree will trust licenses signed with that dev private key.
//
// PRODUCTION HAND-OFF (Phase 4):
//   1. Generate a new RSA-2048 keypair on an offline machine.
//   2. Store the private key in the production password manager / HSM.
//   3. Replace the PEM constant below with the public half.
//   4. Update Fingerprint with the new SHA-256 of the SubjectPublicKeyInfo.
//   5. Re-issue all customer licenses signed by the new key.
//   6. Tag the release "license-key-rotation-N".
//
// Reference: ARCHITECTURE_BLUEPRINT.md §7.2, PHASE1_EXECUTION_PLAN.md B3
// ============================================================================

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Embedded RSA public key used by <see cref="LicenseSignatureValidator"/>.
/// </summary>
/// <remarks>
/// <b>DEV KEY — REPLACE BEFORE PRODUCTION.</b> See file header for the
/// production hand-off procedure.
/// </remarks>
public static class EmbeddedPublicKey
{
    /// <summary>
    /// PEM-encoded RSA public key (SubjectPublicKeyInfo). DEV ONLY.
    /// </summary>
    public const string Pem =
        "-----BEGIN PUBLIC KEY-----\n" +
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAxecX6403YEbcfv/yP4hq\n" +
        "ptqp1mAPeCBlHYpuA1VIbJ/w3e5vLGA3CgkccL0igl1Y+GHIvt0chLnx6nDPYdZd\n" +
        "X9IwMcEuKVvgIKOzoYR2AGkGPpmICC0Yh67LT/yeyshOPGgtcQSbh1WEoG5zdLnv\n" +
        "AKwUmHsG8O32EpWaQiHOx49+yea8iVJxoDswbjgSOeH/M3hNRQ5w00caIzIUvMD5\n" +
        "YLyuGriOzGRkdtbKn0PB9zHEwrW22wrU61OXk1iyk55Mu1pAHvCwgu35W7YeCaeU\n" +
        "s3CoV6ecFADre61u9rxYkgq9cjxd8wCsoVFW2JTN7Gpz7q1RLb6mHmlEhp3MCZrT\n" +
        "AQIDAQAB\n" +
        "-----END PUBLIC KEY-----\n";

    /// <summary>
    /// SHA-256 fingerprint of the SubjectPublicKeyInfo bytes for
    /// <see cref="Pem"/>, as uppercase hex. Test code asserts this value
    /// to make accidental key replacement immediately visible.
    /// </summary>
    public const string Fingerprint =
        "2A5983CBC51FDB77F0D146A8F289A473A15F9F703338B4041D407EA77E010311";
}
