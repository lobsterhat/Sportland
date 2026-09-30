#!/usr/bin/env node
/**
 * Audits and repairs Unity `.meta` sidecar files under Assets/.
 *
 * Unity resolves every asset reference through the GUID in its `.meta`, so a file
 * committed without one gets a different GUID on each machine that imports it, and
 * scene/prefab references break for everyone but the author. The Editor generates
 * metas on import, which covers anyone working in Unity — this exists for changes
 * authored outside the Editor (agents, scripted edits) that need to land complete.
 *
 *   node tools/unity-meta.mjs --check   report missing and orphaned metas (exit 1 if any)
 *   node tools/unity-meta.mjs --fix     write the missing ones it can generate safely
 *
 * --fix deliberately only generates metas for folders and `.cs` scripts. Art and
 * binary assets carry importer settings that only Unity can produce correctly, so
 * those are reported and left alone.
 */

import { randomBytes } from 'node:crypto';
import { readdirSync, existsSync, writeFileSync, statSync } from 'node:fs';
import { join, relative, extname, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const assetsRoot = join(repoRoot, 'Assets');

// Unity's own import-exclusion rules: hidden entries, backup suffixes, and CVS dirs
// are invisible to the asset database, so they must not get metas.
const isIgnored = (name) =>
  name.startsWith('.') || name.endsWith('~') || name.toLowerCase() === 'cvs' || name.endsWith('.tmp');

const newGuid = () => randomBytes(16).toString('hex');

const folderMeta = (guid) => `fileFormatVersion: 2
guid: ${guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;

const scriptMeta = (guid) => `fileFormatVersion: 2
guid: ${guid}
MonoImporter:
  externalObjects: {}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {instanceID: 0}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
`;

function scan(dir, found = { missing: [], orphaned: [] }) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (isIgnored(entry.name)) continue;
    const path = join(dir, entry.name);

    if (entry.name.endsWith('.meta')) {
      const target = path.slice(0, -'.meta'.length);
      if (!existsSync(target)) found.orphaned.push(path);
      continue;
    }

    if (!existsSync(`${path}.meta`)) found.missing.push(path);
    if (entry.isDirectory()) scan(path, found);
  }
  return found;
}

const generatorFor = (path) => {
  if (statSync(path).isDirectory()) return folderMeta;
  return extname(path) === '.cs' ? scriptMeta : null;
};

if (!existsSync(assetsRoot)) {
  console.error('No Assets/ directory here — run this from the Sportland repo.');
  process.exit(2);
}

const fix = process.argv.includes('--fix');
if (!fix && !process.argv.includes('--check')) {
  console.error('Usage: node tools/unity-meta.mjs --check | --fix');
  process.exit(2);
}

const { missing, orphaned } = scan(assetsRoot);
const show = (p) => relative(repoRoot, p).split('\\').join('/');

const generated = [];
const needsUnity = [];

for (const path of missing) {
  const generator = generatorFor(path);
  if (!generator) {
    needsUnity.push(path);
    continue;
  }
  if (fix) writeFileSync(`${path}.meta`, generator(newGuid()));
  generated.push(path);
}

if (generated.length) {
  console.log(fix ? `Wrote ${generated.length} .meta file(s):` : `Missing ${generated.length} .meta file(s):`);
  for (const p of generated) console.log(`  ${show(p)}.meta`);
}

if (needsUnity.length) {
  console.log(`\n${needsUnity.length} asset(s) missing a .meta that only Unity can generate.`);
  console.log('Open the project in the Editor, let it import, and commit the results:');
  for (const p of needsUnity) console.log(`  ${show(p)}`);
}

if (orphaned.length) {
  console.log(`\n${orphaned.length} orphaned .meta file(s) (the asset is gone). Safe to delete:`);
  for (const p of orphaned) console.log(`  ${show(p)}`);
}

if (!generated.length && !needsUnity.length && !orphaned.length) {
  console.log('All Assets/ entries have a matching .meta file.');
  process.exit(0);
}

// --fix reports what it could not handle but still succeeds; --check is the gate.
process.exit(fix && !needsUnity.length && !orphaned.length ? 0 : 1);
