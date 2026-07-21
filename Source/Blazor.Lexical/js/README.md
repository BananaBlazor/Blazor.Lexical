# Blazor.Lexical JS glue

TypeScript source for the ESM glue module that wraps [Lexical](https://lexical.dev).

The bundled output (`../wwwroot/blazor-lexical.mjs`) is **generated, not checked in**
(it is git-ignored). MSBuild regenerates it automatically on build when it is
missing or when `src/` / `package.json` changed — installing the npm packages
first if `node_modules` is absent. So a fresh clone just needs **Node.js on PATH**;
`dotnet build` handles the rest.

## Rebuild the bundle manually

```sh
# from the repo root
pwsh ./Scripts/build-js.ps1        # installs deps if needed, then bundles
```

or directly with npm:

```sh
npm ci        # or: npm install
npm run build
```

Both write `../wwwroot/blazor-lexical.mjs`.

## Update the Lexical version

```sh
pwsh ./Scripts/update-lexical.ps1 -Version 0.49.0
```

Pins `lexical`, `@lexical/rich-text`, `@lexical/history` to the given version,
refreshes `package-lock.json`, and rebuilds the bundle. Commit the changed
`package.json` + `package-lock.json`.

## Layout

- `src/index.ts` — the glue module: `create`, `getText`/`setText`,
  `getHtml`/`setHtml`, `getMarkdown`/`setMarkdown`,
  `getEditorStateJson`/`setEditorStateJson`, `setEditable`, `dispose`.
- `package.json` — pins the Lexical packages (currently 0.48.0).
