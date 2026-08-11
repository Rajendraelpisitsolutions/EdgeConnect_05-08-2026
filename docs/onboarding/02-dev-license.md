# 02 — Dev license

EdgeConnect is license-gated: protocol modules + the Studio itself activate only when the loaded license enables them. In production, customers receive RSA-signed JSON licenses from a production keypair held offline.

For development, **the keypair is checked into the repo**:

| Half | Location |
|---|---|
| Public key (compiled into the binary) | `src/ElpisEdgeConnect.Core/Licensing/EmbeddedPublicKey.cs` |
| Private key (test fixture) | `tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs` |

This is documented as `!!! DEV KEY — REPLACE BEFORE PRODUCTION !!!` in both files. The production hand-off step (Phase 4) rotates the keypair to one held offline / in HSM. Until then, dev runs against the dev key.

**You don't need a password manager** to set up a dev license. Everything you need is in the repo.

## Generate your license

**A license cannot be shared between machines.** Every license carries a `gatewayId` and `LicenseManager` rejects one whose id does not match the gateway it is loaded on (ADR-0036), while the gateway identity itself is a UUID minted on first run on each machine (ADR-0038). So there is no bundled license to hand out — you generate one bound to *your* machine, once:

```pwsh
.\scripts\dev\sign-dev-license.ps1
```

It resolves this machine's gateway identity, signs a long-validity Enterprise license with customer `DevOnly`, every protocol + `connectivity-studio` module enabled and sensible instance limits (50 sources / 5 sinks / 100 routes), and writes it to `data/dev-license.local.json` — which is **gitignored, deliberately**. A machine-bound license in a shared file is worthless to everyone but its owner.

Then point `EDGECONNECT_LICENSE_PATH` at it (the script prints this line for you):

```pwsh
$env:EDGECONNECT_LICENSE_PATH = "$PWD\data\dev-license.local.json"
```

For Linux:

```bash
export EDGECONNECT_LICENSE_PATH="$PWD/data/dev-license.local.json"
```

> **If the script cannot resolve your gateway identity** it stops and says so. The identity is minted on first run, so either start EdgeConnect once and re-run, or pass it explicitly with `-GatewayId <uuid>`. The startup log states it verbatim: `Gateway identity resolved: <uuid>`.

Then continue to [03-dev-config.md](03-dev-config.md).

### Historical note

The repo used to ship `docs/onboarding/dev-license.json`, signed with `gatewayId = "dev-gateway"`. That string is never produced as a machine identity, so the file activated on **no** fresh machine — it only appeared to work where the machine had been licensed before gateway binding landed, or where the identity had been seeded by hand. It was removed rather than regenerated, because any regenerated copy would be bound to whoever ran the script and equally useless to everyone else.

## Verify the license loads

After setting up your dev gateway config (step 03), launching the Studio (step 04) will surface license problems in the startup log:

```
License loaded: customer=DevOnly edition=Enterprise expires=2031-12-31
Modules enabled: source-focas2, source-brother-http, source-modbus-tcp, source-mtconnect,
                 source-s7, source-opc-ua-client, sink-mqtt, sink-opc-ua-server,
                 connectivity-studio (+ historian-bridge reserved)
```

A `license signature invalid` error means the file was edited (the signature breaks on any byte change). Regenerate rather than repair it — the file is local and disposable:

```pwsh
.\scripts\dev\sign-dev-license.ps1
```

A `License '...' is issued for gateway '...', but this gateway's identity is '...'` error means the license was signed for a different machine. Same fix: regenerate.

## Regenerate the dev license

The license expires 5 years from issuance. To regenerate, or to issue one with a different expiry:

```pwsh
.\scripts\dev\sign-dev-license.ps1
.\scripts\dev\sign-dev-license.ps1 -ExpiresOn 2035-01-01
.\scripts\dev\sign-dev-license.ps1 -GatewayId <uuid> -OutPath licenses/other-machine.json
```

Defaults: 5-year expiry, this machine's gateway identity, `data/dev-license.local.json`, every module enabled.

What the script does:

1. Resolves the gateway identity from the same machine-anchored locations `GatewayIdentityStore` reads — `%ProgramData%\Elpis\EdgeConnect\identity`, the HKLM mirror, then `<data root>\identity`.
2. Extracts `TestRsaKeys.PrivatePem` from `tests/ElpisEdgeConnect.Core.Tests/Licensing/TestRsaKeys.cs` into a temp `.pem` file.
3. Invokes `tools/LicenseGen new` with the canonical module key list, customer `DevOnly`, edition `Enterprise`, gateway = the resolved id.
4. Writes the signed JSON to the output path.
5. Scrubs the temp file (no PEM left on disk).

**Do not commit the result.** It works only on the machine that generated it. Each contributor runs the script once.

## Why generating locally is safe

The dev license is signed by the **dev keypair** that is already in the repo, so generating one locally is no weaker than the existing situation — anyone with repo access can already sign whatever license they like using `TestRsaKeys.PrivatePem`.

The `DevOnly` customer field is operator-visible and obvious in any deployment. Production license issuance uses a different keypair generated during the Phase 4 hand-off (per the comment header in `EmbeddedPublicKey.cs`), at which point this dev key and every dev license signed by it become invalid.

Keeping the generated file **out** of git is not a secrecy measure — it is because gateway binding makes a committed copy useless to everyone except the person who generated it, which is exactly how the old `dev-gateway` license came to look valid while working nowhere.

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
