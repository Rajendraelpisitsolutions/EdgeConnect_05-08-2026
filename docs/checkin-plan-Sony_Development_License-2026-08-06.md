# Check-in plan — branch `Sony_Development_License`

**Repo:** <https://github.com/elpisitsolutions/EdgeConnect/tree/Sony_Development_License>
**Prepared:** 2026-08-06
**Local HEAD:** `1f52df3` — *fix(modbus): TL-28 report serial endpoint (COMx) instead of :502*

## Branch state

| Check | Result |
|---|---|
| `origin/Sony_Development_License` vs local `HEAD` (after `git fetch`) | **0 ahead / 0 behind** — every commit is already pushed |
| Tracked files modified but **not committed** | **48** |
| Untracked paths | **9** |
| `dotnet build ElpisEdgeConnect.sln` | **Succeeded — 0 warnings, 0 errors** (verified 2026-08-06) |
| Diff size | +2,593 / −857 across tracked files |

**Conclusion:** nothing is waiting to be *pushed*. Everything below is work sitting in the
working tree that has never been committed. The list is grouped into the commits it should
become, in the order they should land.

---

## 1. Items to check in

### A. Studio operator-UI redesign — 43 files (largest item)

The bulk of the pending work. Left-rail navigation replacing the top bar, pinned page
headings, restyled cards/wizards, and a shared formatting helper so no screen shows a raw
ISO timestamp, un-separated number, or lowercase protocol slug.

| Path | Δ | Note |
|---|---|---|
| `Components/Shared/OperatorFormat.cs` | **new** | Shared operator-facing formatters (`Ago`, byte counts, protocol names). Plain `.cs`, not `.razor`, because the Razor parser misreads `<` in switch expressions. **Referenced by 12 of the modified pages — must land in the same commit or the build breaks.** |
| `wwwroot/css/site.css` | +875 | Rail, heading band, card and wizard styling |
| `Components/Layout/NavMenu.razor` | +305 | Left rail, collapse chevron, Menu/System groups, active-tab bar |
| `Components/Layout/MainLayout.razor` | +140 | Rail + pinned-heading shell |
| `Components/Pages/Onboarding/OnboardingFlow.razor` | +264 | |
| `Components/Pages/Sources.razor` | +120 | |
| `Components/Pages/Diagnostics.razor` | +108 | |
| `Components/Pages/Routes.razor` | +96 | |
| `Components/Pages/RouteDetail.razor`, `SinkDetail.razor`, `Config.razor` | +92 / +90 / +88 | |
| `Components/Pages/Backup.razor`, `Sinks.razor`, `SourceDetail.razor` | +87 / +80 / +78 | |
| 24 further `.razor` files (Overview, RouteCard, Tap, StatusFooter, App, Checklist, Bundle, License, all 10 source wizards, 3 sink wizards, AddRoute, WizardShell, WizardActions, TagBrowseTreeView, SourceLogDialog, OnboardingNavigation, OnboardingProgress) | 1–54 each | |
| `wwwroot/img/elpis-mark.png` | **new** | Rail brand mark — referenced by `NavMenu.razor:32`. **Binary asset; if it is not committed the rail falls back to the "E" tile.** |
| `wwwroot/favicon.ico` | **new** | Referenced by `App.razor:24`. Same rule — commit or the tab icon 404s. |

**Check-in risk:** the two binary assets are untracked and easy to miss with a
`git add -u`. Use explicit `git add` for them.

### B. MQTT client-id collision fix — 3 files

A real field bug, not cosmetics. Default client id was `edgeconnect-{InstanceId}`, which
collides whenever two gateways have a destination of the same name (and the wizard suggests
"MQTT"). Brokers enforce client-id uniqueness, so the two clients evict each other in a loop
and the operator sees `MQTT.PERTAG_PUBLISH_FAILED — client is disconnected` with no cause.

| Path | What changed |
|---|---|
| `src/ElpisEdgeConnect.Sinks.Mqtt/MqttSinkAdapter.cs` | `DefaultClientId()` now includes a sanitised, 24-char-capped machine name; new `_disconnectCount` / `_firstDisconnectAt` counters; `DescribeFlapping()` appends a plain-English cause to error text once drops exceed 5 **and** ~1/min; `disconnectCount` added to diagnostics so the bundle carries flap evidence |
| `src/ElpisEdgeConnect.Sinks.Mqtt/MqttSinkConfiguration.cs` | Doc comment only |
| `src/ElpisEdgeConnect.Management/Wizards/MqttSinkWizardModel.cs` | Doc comment only |

**Gap to close before check-in:** `tests/ElpisEdgeConnect.Sinks.Mqtt.Tests/` exists but
contains **no test for `DefaultClientId` or `DescribeFlapping`**. `DefaultClientId` is already
`internal static` — trivially testable (sanitisation, length cap, empty-hostname fallback).
Per `CLAUDE.md` §7 this behaviour should not ship untested.

**Behaviour note for the release notes:** existing deployments that left Client id blank will
reconnect under a *new* id. Safe (`WithCleanSession(true)`, no orphaned session state), but
broker-side ACLs or dashboards keyed on the old id need updating.

### C. Installer — service recovery + FOCAS2 native libraries

| Path | What changed |
|---|---|
| `installer/ElpisEdgeConnect.wxs` | (1) `util:ServiceConfig` — auto-restart the service on 1st/2nd/3rd failure after 30 s, counter reset daily. (2) `<Files Include="!(bindpath.FocasSrc)\*.dll" />` added to **both** the Management and Host component groups, so `Fwlib64.dll` and its five per-model libraries ship beside the exe |

**Two blockers on this one — fix before committing:**

1. **`FocasSrc` bindpath is not documented.** The build command in the file header
   (`ElpisEdgeConnect.wxs:23-27`) still lists only `HostSrc` and `MgmtSrc`. As written,
   `wix build` fails on an undefined bindpath. Add
   `-bindpath "FocasSrc=<root>/fwlib0iD64"` (or wherever the libraries are staged) to the
   header comment **and** to whatever CI/publish script invokes `wix build`.
2. **Where do the DLLs come from?** See §2, `fwlib0iD64/`.

### D. License Generator tool — product catalogue

| Path | What changed |
|---|---|
| `tools/LicenseGeneratorApp/LicenseDatabase.cs` | New `Products` table + idempotent seed of "Elpis EdgeConnect" and "EREMOS V2"; `GetProductCatalog()` and `AddProduct()` (case-insensitive duplicate guard) |
| `tools/LicenseGeneratorApp/MainForm.cs` | "Add product" row above the Product dropdown (text box + Save, Enter-to-save); dropdown now catalogue-driven; window/header title "Elpis EdgeConnect — License Generator" → **"Elpis — License Generator"**; footer copyright 2024 → 2026 |

**Note:** the schema addition is additive and `CREATE TABLE IF NOT EXISTS` + `INSERT OR IGNORE`,
so an existing `.db` upgrades in place. Worth a smoke test against a real existing database
before check-in.

### E. Copyright / branding

| Path | What changed |
|---|---|
| `Directory.Build.props` | `Copyright © 2024` → `© 2026` — stamps every assembly in the solution |

Pairs with the footer string in `MainForm.cs`. Commit them together or note the split.

---

## 2. Untracked paths — decide before adding

| Path | Size | Recommendation |
|---|---|---|
| `fwlib0iD64/` — `Fwlib64.dll`, `fwlib0DN64.dll`, `fwlib0iD64.dll`, `fwlib30i64.dll`, `fwlibNCG64.dll`, `fwlibe64.dll` | **~11.5 MB, 6 binaries** | **Needs an explicit decision.** These are FANUC-licensed native libraries, and the installer change in item C now depends on them being staged somewhere. Options: (a) commit them (repo grows 11.5 MB permanently, FANUC redistribution terms apply), (b) keep them out of git and have the build/publish script fetch or copy them from a known location, adding `fwlib0iD64/` to `.gitignore`. **Nothing in `.gitignore` currently covers them, so they will be swept in by `git add .`.** Today no FOCAS binary is tracked in the repo. |
| `docs/UI-Changes-Only-Sony-vs-Rajendra-2026-08-05.md` | text | Commit — useful record of the Sony vs Rajendra UI divergence |
| `docs/UI-Comparison-Sony-vs-Rajendra-2026-08-05.md` | text | Commit |
| `docs/UI-Changes-Only-Sony-vs-Rajendra-2026-08-05.docx` | binary | Generated from the `.md`. Suggest **not** committing — keep one source of truth and regenerate. |
| `docs/UI-Comparison-Sony-vs-Rajendra-2026-08-05.docx` | binary | Same |
| `docs/EdgeConnect-Studio-UI-Change-Report.docx` | binary | Same — has no `.md` counterpart, so if it is the only copy, either commit it or export a `.md` first |
| `.claude/settings.json` | text | Project-level Claude Code permission allowlist (`git fetch/push/rev-list`, `dotnet test`, …). Shared dev convenience — commit it if the team wants it, otherwise `.gitignore` it. Contains no secrets. |

---

## 3. Pre-check-in gate

Run in this order:

- [ ] `dotnet build ElpisEdgeConnect.sln` → 0 warnings, 0 errors *(passing as of 2026-08-06)*
- [ ] `dotnet test --filter "Category!=Flaky"` — full run not yet executed against this working tree
- [ ] Add the `DefaultClientId` / `DescribeFlapping` tests (item B gap)
- [ ] Fix the `FocasSrc` bindpath documentation + build script (item C blocker 1)
- [ ] Decide the `fwlib0iD64/` question (§2) and update `.gitignore` either way
- [ ] Build the MSI end-to-end and confirm the FOCAS DLLs land beside both exes and that
      service recovery is registered (`sc qfailure ElpisEdgeConnect`)
- [ ] Smoke-test the License Generator against an existing `.db`
- [ ] Note the MQTT client-id change in the release notes (broker ACL impact)
- [ ] Confirm the CRLF/LF warnings on 11 `.razor` files are just line-ending normalisation and
      not unintended whole-file rewrites (`git diff --stat --ignore-cr-at-eol`)

## 4. Suggested commit sequence

1. `feat(studio): operator-UI redesign — left rail, pinned headings, shared formatters` (item A, 43 files)
2. `fix(mqtt): make the default client id unique per machine and name the flap cause` (item B + its tests)
3. `build: stamp 2026 copyright across all assemblies` (item E)
4. `feat(installer): auto-restart the service and ship the FOCAS2 native libraries` (item C, after both blockers are cleared)
5. `feat(tools): catalogue-driven product list in the License Generator` (item D)
6. `docs: Sony vs Rajendra UI comparison` (§2 text files)

Items 1–3 and 5 are independently shippable today. Item 4 is blocked.
