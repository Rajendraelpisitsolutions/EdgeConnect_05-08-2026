# EdgeConnect Onboarding — full guide

The entry point is the repo-root [`ONBOARDING.md`](../../ONBOARDING.md). This directory holds the deep-dives each step refers to.

## Reading order

```
00-prerequisites.md         What you need installed before anything else.
01-clone-build-test.md      Get the repo, build it, run the unit suite.
02-dev-license.md           Use the bundled dev license (or regenerate one).
03-dev-config.md            Set up the dev gateway config + identity file.
04-running-studio.md        Launch the Studio and walk through the demo.
05-mqtt-integration-tests.md  Install Mosquitto and run the MQTT integration suite.
06-codebase-tour.md         Where things live, in 10 minutes.
07-conventions.md           Branch / commit / PR conventions.
08-troubleshooting.md       Things that go wrong, and how to fix them.
```

## Bundled artifacts

- Dev license — **not committed**. Licenses are bound to one machine (ADR-0036 + ADR-0038), so each contributor generates their own with `.\scripts\dev\sign-dev-license.ps1` into the gitignored `data/`. See [02-dev-license.md](02-dev-license.md).
- [`../../scripts/dev/bootstrap.ps1`](../../scripts/dev/bootstrap.ps1) — prereq check + first build + first test.
- [`../../scripts/dev/setup-dev-config.ps1`](../../scripts/dev/setup-dev-config.ps1) — set up a fresh `data/` root.
- [`../../scripts/dev/templates/dev-current.json`](../../scripts/dev/templates/dev-current.json) — minimal gateway config the setup script seeds.
