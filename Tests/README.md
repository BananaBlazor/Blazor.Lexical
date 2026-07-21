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

`HarnessFixture` boots **Tests.Integration.Host** in-process on a real Kestrel port
(via `WebApplicationFactory<Program>`, so the Blazor Server circuit's WebSocket
works) and launches a headless browser with [Playwright](https://playwright.dev/dotnet/).
Tests drive the dedicated harness page at `/interop-harness`
(`Integration.Host/Components/Pages/InteropHarness.razor`), which exposes each
touchpoint with stable element ids.

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
