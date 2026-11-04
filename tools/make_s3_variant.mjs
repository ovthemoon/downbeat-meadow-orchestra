#!/usr/bin/env node
// Creates a patched copy of the Unity project for S-3 size experiments (the main project stays untouched).
//   --postprocess off   drop the URP PostProcessData reference (film grain / SMAA textures) and strip unused post-processing variants
//   --builtin           remove URP completely (Built-in render pipeline, empty scene with one camera)
// Usage: node tools/make_s3_variant.mjs --dest /path/to/copy [--postprocess off] [--builtin]
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';

const { values } = parseArgs({
  options: {
    dest: { type: 'string' },
    postprocess: { type: 'string', default: 'on' },
    builtin: { type: 'boolean', default: false },
  },
});
if (!values.dest) throw new Error('--dest is required');

const source = resolve(import.meta.dirname, '..', 'unity', 'Downbeat');
const dest = resolve(values.dest);
const SKIP = new Set(['Library', 'Temp', 'Logs', 'Builds', 'UserSettings', 'obj', 'Build', '.vscode']);
const SKIP_SUFFIXES = ['.csproj', '.slnx', '.sln'];

function patch(file, transform) {
  const path = join(dest, file);
  writeFileSync(path, transform(readFileSync(path, 'utf8')));
}

function replaceOrThrow(text, pattern, replacement, label) {
  if (!pattern.test(text)) throw new Error(`pattern for ${label} not found`);
  return text.replace(pattern, replacement);
}

rmSync(dest, { recursive: true, force: true });
mkdirSync(dest, { recursive: true });
cpSync(source, dest, { recursive: true, filter: (src) => !SKIP.has(src.slice(source.length + 1).split('/')[0]) && !SKIP_SUFFIXES.some((x) => src.endsWith(x)) });

if (values.postprocess === 'off' && !values.builtin) {
  patch('Assets/Settings/Renderer2D.asset', (t) => replaceOrThrow(t, /m_PostProcessData: \{[^}]*\}/, 'm_PostProcessData: {fileID: 0}', 'postProcessData'));
  patch('Assets/Settings/UniversalRP.asset', (t) => t.replaceAll('m_StripUnusedPostProcessingVariants: 0', 'm_StripUnusedPostProcessingVariants: 1'));
}

if (values.builtin) {
  const manifestPath = join(dest, 'Packages', 'manifest.json');
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  delete manifest.dependencies['com.unity.render-pipelines.universal'];
  writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));
  rmSync(join(dest, 'Packages', 'packages-lock.json'), { force: true });
  for (const p of ['Assets/Settings', 'Assets/Settings.meta', 'Assets/UniversalRenderPipelineGlobalSettings.asset', 'Assets/UniversalRenderPipelineGlobalSettings.asset.meta',
    'Assets/DefaultVolumeProfile.asset', 'Assets/DefaultVolumeProfile.asset.meta', 'Assets/Scenes', 'Assets/Scenes.meta', 'ProjectSettings/URPProjectSettings.asset']) {
    if (existsSync(join(dest, p))) rmSync(join(dest, p), { recursive: true, force: true });
  }
  patch('ProjectSettings/GraphicsSettings.asset', (t) => t.replace(/(m_RenderPipelineGlobalSettingsMap:\n)\s+UnityEngine\.Rendering\.Universal\.UniversalRenderPipeline:[^\n]*\n/, '$1  {}\n').replace('m_RenderPipelineGlobalSettingsMap:\n  {}', 'm_RenderPipelineGlobalSettingsMap: {}'));
  patch('ProjectSettings/QualitySettings.asset', (t) => t.replace(/customRenderPipeline: \{[^}]*\}/g, 'customRenderPipeline: {fileID: 0}'));
}
console.log(`variant written to ${dest} (postprocess=${values.postprocess}, builtin=${values.builtin})`);
