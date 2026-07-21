# Contributing to Blazor.Lexical

## Repository layout

| Path | What it is |
| --- | --- |
| `Source/Blazor.Lexical/` | The shipped RCL — the product. |
| `Web/` | The public website: the WebAssembly demo plus DocFX docs under `/docs/`, deployed to GitHub Pages on push to `main`. Product-tier, not a sample. |
| `Samples/Extensions.Badge/` | The reference consumer extension — the worked example for the extension SDK, built only against the library's public surface. Not packed. |
| `Tests/UnitTests/` | xUnit tests (options, DI, theme serialization). |
| `Tests/Integration/` | Playwright end-to-end tests driving the harness. |
| `Tests/Integration.Host/` | Blazor Server host for the integration harness (`/interop-harness`), booted in-process by `Tests/Integration`. |
| `Scripts/` | JS build (`build-js.ps1`) and Lexical version bump (`update-lexical.ps1`). |

Samples and tests are named `<top-level folder>.<subfolder>` —
`Samples.Extensions.Badge`, `Tests.UnitTests`, `Tests.Integration`,
`Tests.Integration.Host`. New ones follow that rule. Only shipped projects carry
the product name (`Blazor.Lexical`, `Blazor.Lexical.Web`).

## Prerequisites

The .NET 10 SDK, and **Node on PATH** — the `BuildLexicalJsBundle` MSBuild target
rebuilds the JS bundle (`wwwroot/blazor-lexical.mjs`) from TypeScript via
`Scripts/build-js.ps1`. A fresh clone needs nothing else; `npm ci` runs
automatically when `node_modules` is missing.

## Build and test

```
dotnet build Blazor.Lexical.slnx
dotnet test Tests/UnitTests/Tests.UnitTests.csproj      # fast, no browser
dotnet test Tests/Integration/Tests.Integration.csproj  # Playwright
dotnet test Blazor.Lexical.slnx                         # everything
dotnet run --project Web                                # the demo site (WebAssembly)
```

The integration tests drive system Chrome (override with `LEXICAL_TEST_CHROME`), so
no `playwright install` step is needed. The harness includes a no-callback editor
(`.harness-format`) and an opt-in editor (`.harness-notify`) that together prove the
library's interop invariants — keep both when editing it.

`TreatWarningsAsErrors` is on repo-wide, as is `GenerateDocumentationFile`, so an
undocumented public member fails the build. Keep the build clean.

Internal design and architectural invariants for the editor, toolbar, JS glue, and
interop live in `Source/Blazor.Lexical/AGENTS.md` — read it before changing them. The
reasoning behind those rules is written up in
[Architecture](Source/Blazor.Lexical/docs/architecture.md), alongside deeper reference on
the JS contract, the extension model, and the built-in features. The overriding invariant:
**no JS→.NET calls unless the consumer opts in.**

## Versioning

The NuGet version is **`Major.<Lexical-minor>.Serial`** — Lexical `0.48.0` gives
`0.48.<serial>`.

There is one source of truth for "which Lexical": the committed
`Source/Blazor.Lexical/js/package-lock.json`. The `.csproj` parses the resolved
`lexical` version from it at evaluation time and derives *everything* from it — the
package version's middle component, the `LexicalPackage.LexicalVersion` const, the
`[assembly: AssemblyMetadata("LexicalVersion", …)]`, the `<Description>` /
`<PackageTags>` / `<PackageReleaseNotes>`, and the footer appended to the packed
README. Never hand-edit any of these; change the pin and rebuild.

- **Major** is locked to `0` in `Source/Blazor.Lexical/Version.props`. Bumping it is
  a deliberate, human-only decision (a breaking C# API change, or Lexical shipping
  its own 1.0). Scripts and CI must not touch it.
- **Serial** is the other value in `Version.props` — our revision within a
  Lexical-minor line. Bump it per release.

Bump Lexical with:

```
Scripts/update-lexical.ps1 -Version x.y.z
```

It re-pins every `@lexical/*` in lockstep, refreshes the lockfile, rebuilds the
bundle, and resets the serial to 0 when the Lexical *minor* changes.

**Publish gate:** the `VerifyLexicalAlignment` target runs
`Scripts/lexical-version.mjs` on every build and before `pack`. It fails the build if
any `lexical` / `@lexical/*` is pinned with a range operator, is not in lockstep, or
is out of sync between `package.json` and the lockfile — so a package can never ship
claiming a Lexical version it doesn't bundle.

## License

By contributing you agree that your contributions are licensed under the MIT
License, per [LICENSE](LICENSE).
