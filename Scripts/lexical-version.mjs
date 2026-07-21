// Single source of truth for the bundled Lexical version + the publish-time
// alignment gate.
//
// Prints the resolved `lexical` version to stdout (nothing else); all diagnostics
// go to stderr. Exits non-zero — failing the build/pack — when the pinned Lexical
// dependency graph is inconsistent, so a package can never ship claiming a Lexical
// version it does not actually bundle. The MSBuild build derives the version itself
// from package-lock.json; this script is the cross-check that guards that derivation.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const jsDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'Source', 'Blazor.Lexical', 'js');
const read = (name) => JSON.parse(readFileSync(join(jsDir, name), 'utf8'));

const pkg = read('package.json');
const lock = read('package-lock.json');

const errors = [];

// The Lexical family = every dependency named `lexical` or `@lexical/*`. Discovered
// from package.json so this never drifts from the actual dependency list.
const family = Object.keys(pkg.dependencies ?? {}).filter(
  (name) => name === 'lexical' || name.startsWith('@lexical/'),
);
if (family.length === 0) {
  errors.push('package.json declares no lexical / @lexical/* dependencies.');
}

const resolvedOf = (name) => lock.packages?.[`node_modules/${name}`]?.version;
const resolvedVersions = new Set();

for (const name of family) {
  const declared = pkg.dependencies[name];
  const resolved = resolvedOf(name);

  if (!resolved) {
    errors.push(`${name}: no resolved version in package-lock.json (run 'npm install').`);
    continue;
  }
  resolvedVersions.add(resolved);

  if (/^[\^~]/.test(declared)) {
    errors.push(
      `${name}: pinned with a range operator ('${declared}'). Lexical packages ship in ` +
        `lockstep and must be pinned to an exact version.`,
    );
  }
  const declaredExact = declared.replace(/^[\^~]/, '');
  if (declaredExact !== resolved) {
    errors.push(
      `${name}: package.json pins '${declared}' but the lockfile resolved '${resolved}' ` +
        `(run 'npm install').`,
    );
  }
}

if (resolvedVersions.size > 1) {
  errors.push(
    `Lexical packages are not in lockstep: found versions ${[...resolvedVersions]
      .sort()
      .join(', ')}. Every lexical / @lexical/* package must resolve to the same version.`,
  );
}

const version = resolvedOf('lexical');
if (!version) {
  errors.push("Could not resolve the 'lexical' package version from package-lock.json.");
}

if (errors.length > 0) {
  console.error('Lexical version alignment check FAILED:');
  for (const message of errors) console.error(`  - ${message}`);
  console.error(
    "Fix the pins in Source/Blazor.Lexical/js/package.json and run 'npm install' " +
      '(or Scripts/update-lexical.ps1 -Version x.y.z).',
  );
  process.exit(1);
}

process.stdout.write(version);
