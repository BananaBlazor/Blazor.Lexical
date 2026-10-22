# Blazor.Lexical — project guide

A Blazor Razor Class Library wrapping the
[Lexical](https://github.com/facebook/lexical) rich-text editor.

## Layout

- **`Source/Blazor.Lexical/`** — the shipped RCL (the product). Its internal design and
  architectural invariants live in `Source/Blazor.Lexical/AGENTS.md` — **read that before
  changing the editor, toolbar, JS glue, or interop.** (Nested agent files aren't picked
  up automatically by every tool, so open it explicitly.) It routes to deeper reference
  under `Source/Blazor.Lexical/docs/`, which is read on demand rather than auto-loaded.
- **`Web/`** (`Blazor.Lexical.Web`) — the project's public website: the Blazor
  WebAssembly demo plus the DocFX docs under `/docs/`, published to GitHub Pages by
  `.github/workflows/deploy-pages.yml`. Product-tier, not a sample.
- **`Samples/`** — reference *consumer extensions*, each built only against the
  library's public surface. Not packed; referenced by the website.
  - `Extensions.Badge` — the worked example for the extension SDK (also used by the
    test harness).
  - `Extensions.GutterMarkers` — host content in the per-block gutter rails.
  - `Extensions.ReferenceFields` — the `setup.primitives` worked example (ghost
    completion + entity commit); driven by the harness's reference-fields page.
- **`Tests/UnitTests`** — xUnit v3 unit tests (options, DI, theme serialization,
  content/tokens, extension registration, packed-layout checks).
- **`Tests/Integration`** — Playwright end-to-end tests driving the harness.
- **`Tests/Integration.Host`** — Blazor Server host for the integration-test
  harness (`/harness/*`, indexed at `/interop-harness`); booted in-process by
  `Tests/Integration`.
- **`Scripts/`** — JS build (`build-js.ps1`), Lexical version bump
  (`update-lexical.ps1`), and the Lexical lockstep/publish gate (`lexical-version.mjs`).

## Build

- `dotnet build Blazor.Lexical.slnx` — builds everything. The `BuildLexicalJsBundle`
  MSBuild target rebuilds the JS bundle (`wwwroot/blazor-lexical.mjs`) from TypeScript
  via `Scripts/build-js.ps1`, so **Node must be on PATH**. A fresh clone needs nothing
  else — `npm ci` runs automatically when `node_modules` is missing.
- `TreatWarningsAsErrors=true` everywhere; keep the build clean.

## Test

Tests use xUnit v3 on Microsoft.Testing.Platform (opted in by `global.json`), so
`dotnet test` takes `--project`/`--solution`, not a bare path; filter with xUnit's
`--filter-class`/`--filter-method`, and TRX comes from `--report-xunit-trx`.

- `dotnet test --project Tests/UnitTests/Tests.UnitTests.csproj` — fast, no browser.
- `dotnet test --project Tests/Integration/Tests.Integration.csproj` — Playwright; boots
  `Tests/Integration.Host` in-process and drives one harness page per feature area
  under `/harness/*` (stable element ids). **One page per test class**: a class
  declares its `Route` and `ReadySelector`, so it boots only the editors it drives.
  Test classes carry no `[Collection]` and run in parallel over a single shared host
  and browser (`HarnessServer`, torn down by the `HarnessShutdown` assembly fixture).
  Without system Chrome at the default path, Playwright's Chromium is used — install it
  with the `playwright.ps1` in the test output dir after any `Microsoft.Playwright`
  bump. The harness includes a no-callback editor (`.harness-format`) and an opt-in
  editor (`.harness-notify`) that together prove the library's interop invariants —
  keep both when editing it.
- A test that fails only under the full parallel run is usually a real race: a late
  editor update (e.g. a trailing `selectionchange`) undoing a key's effect. Fix it in
  the library rather than adding waits to the test.
- Run the website by hand: `dotnet run --project Web` (WebAssembly).

## Docs

Two tiers, kept separate: `Web/docfx/` is consumer-facing SDK documentation, published to
the docs site. `Source/Blazor.Lexical/docs/` is contributor reference — internal
mechanics plus `architecture.md`, the reasoning behind the invariants in
`Source/Blazor.Lexical/AGENTS.md`. When a rule changes, update the agent file *and* the
architecture page: one carries the constraint, the other the reasoning.

The docs site only builds with `-p:BuildDocs=true` (e.g. `dotnet publish Web -c Release
-p:BuildDocs=true`), via DocFX pinned as a local tool in `.config/dotnet-tools.json`.

## Shell

Commands run from the repo root already — do not prefix with `cd`.
