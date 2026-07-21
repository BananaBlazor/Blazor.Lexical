# Tests

Two projects, added to `Blazor.Lexical.slnx`:

| Project | What it covers |
| --- | --- |
| `UnitTests/Tests.UnitTests.csproj` | Pure C#, no JS: `LexicalOptions` defaults and `AddLexicalBlazor` DI registration. |
| `Integration/Tests.Integration.csproj` | Every Blazor ↔ Lexical-JS touchpoint, driven through a real browser. |

Run everything:

```sh
dotnet test Blazor.Lexical.slnx
```

## Integration tests — how they work

`HarnessServer` boots **Tests.Integration.Host** in-process on a real Kestrel port
(via `WebApplicationFactory<Program>`, so the Blazor Server circuit's WebSocket
works) and launches a headless browser with [Playwright](https://playwright.dev/dotnet/).
Both are process-wide singletons, started once and shut down once the assembly's run
ends; `HarnessFixture` is the per-class handle on them.

Tests drive a harness page per feature area under
`Integration.Host/Components/Pages/Harness/`, each exposing its touchpoints with
stable element ids. `/interop-harness` is now just an index of them.

### One page per test class

A test class derives from `HarnessTestBase` and declares the page it drives:

```csharp
protected override string Route => "harness/marks";

protected override string ReadySelector =>
    "#editor-marks-quiet[data-lexical-editor='true']";
```

`OpenAsync()` then opens that route in a fresh browser context and waits on the
selector — conventionally the *last* editor declared on the page, since editors boot in
document order. Because a test only boots the editors its own feature needs, pages stay
small and load fast. Two sections are shared where more than one class needs the same
editor under the same ids: `MainEditorSection` and `NotifyEditorSection`, under
`Integration.Host/Components/Harness/`.

### Parallelism

Test classes carry no `[Collection]` attribute, so each is its own xunit collection and
they run concurrently (capped by `maxParallelThreads` in `xunit.runner.json`) against
the one shared host and browser. `HarnessTestFramework` is the assembly-wide teardown
hook that closes them.

**Browser:** uses the system Chrome at
`C:\Program Files\Google\Chrome\Application\chrome.exe` by default. Override with
the `LEXICAL_TEST_CHROME` environment variable to point at a different Chromium
executable. (No `playwright install` needed — only the bundled driver is used.)

### Touchpoints asserted

- **Module functions:** `create`, `getText`, `setText`, `setEditable`, `dispose`,
  and Id-keyed instance isolation.
- **Parameters:** `Placeholder`, `CssClass`, `Theme` (→ Lexical node classes),
  `ReadOnly` (create-time + dynamic toggle), `EnableHistory` (undo on/off).
- **Callback:** `OnContentChanged` fires with text, on both typing and `SetText`,
  and is debounced.

## After an `npm` update

The point of the integration suite: after `Scripts/update-lexical.ps1 -Version x.y.z`
rebuilds the bundle, run `dotnet test Blazor.Lexical.slnx`. A red test means a
Lexical API the wrapper depends on changed shape or behavior.
