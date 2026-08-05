# EdgeConnect Studio — UI reconstruction prompt

Copy everything below the line into the agent. It front-loads every fact the
agent would otherwise spend tokens rediscovering: stack, metrics, tokens,
vocabulary, known defects. Do not ask it to "explore the codebase first".

---

## ROLE

You are redesigning the operator UI of **Elpis EdgeConnect Connectivity Studio** —
a Blazor Server app that industrial engineers use on a factory floor to connect
PLCs/CNCs to data pipelines and diagnose why data stopped flowing.

Audience: a maintenance engineer under time pressure, not a developer.
Success = they can answer *"what is broken and what do I do next?"* in <5 seconds.

## STACK (do not re-derive)

| | |
|---|---|
| Framework | Blazor Server, .NET 8, interactive server rendering |
| UI kit | MudBlazor 7.15 (`MudPaper`, `MudTable`, `MudChip`, `MudButton`, `MudAlert`) |
| Root | `src/ElpisEdgeConnect.Management/Components/` |
| Global CSS | `wwwroot/css/site.css` (loads AFTER MudBlazor.min.css, so it wins at equal specificity) |
| Layout | `Layout/MainLayout.razor`, `Layout/NavMenu.razor`, `Layout/StatusFooter.razor` |
| Browser | Edge/Chrome 105+, so `:has()` is available |

## LOCKED METRICS (measured — do not guess)

| Element | Value |
|---|---|
| AppBar | **48px** — it carries `mud-appbar-dense`; MudBlazor computes `calc(var(--mud-appbar-height) - var(--mud-appbar-height)/4)` from a 64px base |
| StatusFooter | **32px**, `position:fixed; bottom:0` |
| MudMainContent | carries `pt-16 pb-12` (64px/48px) — **16px more than the AppBar**, which is a real mismatch, not a rounding error |
| Sticky offsets | resolve against `.mud-main-content`, NOT the viewport |

**Trap:** `body{overflow:hidden}` makes MudBlazor's scroll lock inert — it tests
`window.innerWidth > document.body.clientWidth`. Dialogs then stop freezing the
page behind them. If you build an app shell, restore it with:
`body.mud-scroll-locked .mud-main-content { overflow:hidden !important; }`

## DESIGN TOKENS (from the existing MudTheme — keep or evolve deliberately)

```
Primary #1976D2   Success #16A34A   Warning #F59E0B   Error #DC2626
Text    #1F2937   Secondary #6B7280 Lines   #E5E7EB
Background #F8FAFC  Surface #FFFFFF
Font: Inter, system-ui, Segoe UI
```

Aesthetic: white chrome, hairline borders, blue reserved for accents and never
for chrome fills (Linear/Stripe/Vercel register). Semantic colours are for state
only and are not the accent.

## VOCABULARY (operator-facing text)

| Never show | Always show |
|---|---|
| sink | Destination |
| instance / instanceId | Device / name |
| adapter, fanout, cursor, draft-apply pipeline | (never surface) |
| `state: 2` | `Running` |
| `2026-08-03T11:42:04.1222918Z` | `2 min ago` (absolute on hover) |
| `sizeBytes: 1257714` | `1.2 MB` |

## KNOWN DEFECTS — fix these, do not spend tokens rediscovering them

| # | Defect | Where |
|---|---|---|
| 1 | **261 icon-only buttons have zero `aria-label` and zero `title`** across Overview/Sources/Sinks/Routes/Diagnostics | all pages |
| 2 | Stale `lastError` rendered as if current — a 19-min-old error displayed as a live fault | Overview, RouteCard |
| 3 | Raw counters with no interpretation. "10,000 in queue" actually meant a wedged route silently dropping data, and nothing said so | Overview, Diagnostics |
| 4 | Numbers without units, separators, or a good/bad frame | throughout |
| 5 | Errors state a symptom, never a remedy | throughout |
| 6 | No empty states — a new gateway with zero sources shows a blank page | Sources, Sinks, Routes |
| 7 | Nav had no active-tab indicator at all (`MudButton` is not `NavLink`) | NavMenu |
| 8 | OPC UA "Test Connection" can never succeed against a cert-requiring server: `OpcUaClientTestConnectionService.cs:260` uses a per-probe instance id that mints a throwaway certificate each click. UI must warn, not just show the opaque security error | OPC UA wizard |
| 9 | Wizard fields lack placeholder/example/help — an operator cannot guess a NodeId or Modbus register format | all wizards |

## PAGES — one job each

| Page | Its single job |
|---|---|
| `Overview.razor` | Is anything broken right now? |
| `Routes.razor` / `RouteDetail.razor` | Where in source→buffer→destination is the problem? |
| `Sources.razor` / `SourceDetail.razor` | Is this device connected and reading? |
| `Sinks.razor` / `SinkDetail.razor` | Is data arriving at the destination? |
| `Diagnostics.razor` | Why did data stop? |
| `Tap.razor` | What values are actually flowing, right now? |
| `SourceWizards/*` | Connect a device without reading a manual |
| `Onboarding/*` | First device connected in under 5 minutes |
| `Config` / `Backup` / `Bundle` / `License` | Change safely; prove what changed |

## RULES

1. **Do not restructure the data layer.** UI only. Core is protocol-agnostic; adapters must not be touched.
2. **State must never rely on colour alone** — pair every colour with text or an icon.
3. **Every number gets a unit and a frame.** "10,000 queued (max 10,000 — dropping)" beats "10000".
4. **Every error gets a next step.**
5. **Every destructive action states its consequence** before confirming.
6. **Preserve all `data-testid` attributes** — tests depend on them.
7. Respect `prefers-reduced-motion`; give keyboard focus a visible ring.
8. Do not introduce a CDN font — CSP blocks it; use a system stack or inline as data URI.

## DELIVERABLE FORMAT

For each change output exactly:

```
FILE: <path>
TYPE: CSS | COMPONENT        # CSS = ships without rebuild; COMPONENT = needs build
SEVERITY: HIGH | MED | LOW
WHY: <one line, operator-facing>
DIFF:
<unified diff>
```

Group all `TYPE: CSS` first — they deploy immediately as static assets.
No prose commentary between items. No "here is what I did" preamble.

## EXECUTION

Work the pages in parallel, one agent per row of the PAGES table. Each returns
only the deliverable blocks above. Do not re-read files another agent owns.
