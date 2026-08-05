# 02 — Dev license

EdgeConnect is license-gated: protocol modules + the Studio itself activate only when the loaded license enables them. In production, customers receive RSA-signed JSON licenses from a production keypair held offline.

For development, **the keypair is checked into the repo**:

| Half | Location |
|---|---|
| Public key (compiled into the binary) | `src/ElpisEdgeConnect.Core/Licensing/EmbeddedPublicKey.cs` |
| Private key (test fixture) | `tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs` |

This is documented as `!!! DEV KEY — REPLACE BEFORE PRODUCTION !!!` in both files. The production hand-off step (Phase 4) rotates the keypair to one held offline / in HSM. Until then, dev runs against the dev key.

**You don't need a password manager** to set up a dev license. Everything you need is in the repo.

## Use the bundled license

The repo ships `docs/onboarding/dev-license.json` — a long-validity Enterprise license signed by the dev key, customer `DevOnly`, every protocol + `connectivity-studio` module enabled, sensible instance limits (50 sources / 5 sinks / 100 routes).

Point `EDGECONNECT_LICENSE_PATH` at it:

```pwsh
$env:EDGECONNECT_LICENSE_PATH = "$PWD\docs\onboarding\dev-license.json"
```

For Linux:

```bash
export EDGECONNECT_LICENSE_PATH="$PWD/docs/onboarding/dev-license.json"
```

Then continue to [03-dev-config.md](03-dev-config.md).

## Verify the license loads

After setting up your dev gateway config (step 03), launching the Studio (step 04) will surface license problems in the startup log:

```
License loaded: customer=DevOnly edition=Enterprise expires=2031-12-31
Modules enabled: source-focas2, source-brother-http, source-modbus-tcp, source-mtconnect,
                 source-s7, source-opc-ua-client, sink-mqtt, sink-opc-ua-server,
                 connectivity-studio (+ historian-bridge reserved)
```

A `license signature invalid` error means the file was edited (signature breaks immediately on any change). Restore from git:

```pwsh
git checkout docs/onboarding/dev-license.json
```

## Regenerate the dev license

The bundled license expires 5 years from issuance. To regenerate (or to issue a fresh one with a different expiry):

```pwsh
.\scripts\dev\sign-dev-license.ps1
```

Defaults: 5-year expiry, writes to `docs/onboarding/dev-license.json`, every module enabled. Override:

```pwsh
.\scripts\dev\sign-dev-license.ps1 -ExpiresOn 2035-01-01 -OutPath licenses/dev-2035.json
```

What the script does:

1. Extracts `TestRsaKeys.PrivatePem` from `tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs` into a temp `.pem` file.
2. Invokes `tools/LicenseGen new` with the canonical module key list, customer `DevOnly`, gateway `dev-gateway`, edition `Enterprise`.
3. Writes the signed JSON to the output path.
4. Scrubs the temp file (no PEM left on disk).

Commit the new `dev-license.json` and the change propagates to every contributor.

## Why this is safe to commit

The dev license is signed by the **dev keypair** that's already in the repo. Adding the signed license JSON to git is no weaker than the existing situation (anyone with repo access can already sign whatever license they want using `TestRsaKeys.PrivatePem`).

The `DevOnly` customer field is operator-visible and obvious in any deployment. Production license issuance uses a different keypair generated during the Phase 4 hand-off (per the comment header in `EmbeddedPublicKey.cs`), at which point this dev key + every dev license signed by it become invalid.

## What changes at production hand-off

Per `EmbeddedPublicKey.cs` line 14-19:

```
PRODUCTION HAND-OFF (Phase 4):
  1. Generate a new RSA-2048 keypair on an offline machine.
  2. Store the private key in the production password manager / HSM.
  3. Replace the PEM constant in EmbeddedPublicKey.cs with the public half.
  4. Update Fingerprint with the new SHA-256 of the SubjectPublicKeyInfo.
  5. Re-issue all customer licenses signed by the new key.
  6. Tag the release "license-key-rotation-N".
```

The dev keypair stays in the repo — dev / CI / local builds continue to work against it. Production builds use a different `EmbeddedPublicKey.Pem` and reject anything signed by the dev key.

## Done?

Continue to [03-dev-config.md](03-dev-config.md).
