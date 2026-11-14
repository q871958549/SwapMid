import { cp, mkdir, readdir, rm, stat } from 'node:fs/promises'
import path from 'node:path'

export const DEFAULT_STATIC_KEEP = 5

function keepCount(raw) {
  const parsed = Number(raw)
  if (!Number.isFinite(parsed)) return DEFAULT_STATIC_KEEP
  return Math.min(8, Math.max(1, Math.floor(parsed)))
}

export function safeGenerationName(buildId) {
  const name = String(buildId ?? '').trim()
  if (!/^[A-Za-z0-9._-]{1,128}$/.test(name)) {
    throw new Error('invalid next static generation name')
  }
  return name
}

async function copyMissing(fromDir, toDir) {
  await mkdir(toDir, { recursive: true })
  const entries = await readdir(fromDir, { withFileTypes: true })
  for (const entry of entries) {
    const from = path.join(fromDir, entry.name)
    const to = path.join(toDir, entry.name)
    if (entry.isDirectory()) {
      await copyMissing(from, to)
      continue
    }
    if (!entry.isFile()) continue
    try {
      await stat(to)
    } catch (error) {
      if (error && error.code === 'ENOENT') await cp(from, to)
      else throw error
    }
  }
}

async function listGenerations(archiveDir) {
  let entries
  try {
    entries = await readdir(archiveDir, { withFileTypes: true })
  } catch (error) {
    if (error && error.code === 'ENOENT') return []
    throw error
  }
  const generations = []
  for (const entry of entries) {
    if (!entry.isDirectory()) continue
    const dir = path.join(archiveDir, entry.name)
    const info = await stat(dir)
    generations.push({ name: entry.name, dir, mtimeMs: info.mtimeMs })
  }
  generations.sort((left, right) => right.mtimeMs - left.mtimeMs || right.name.localeCompare(left.name))
  return generations
}

/**
 * 把当前构建的 /_next/static 记入卷，并把它和最近几代一起铺回运行目录。
 * 内容哈希文件互不覆盖，旧页面继续能拿到自己的 chunk。未配置归档目录时直接跳过。
 */
export async function retainNextStaticGenerations({
  staticDir,
  archiveDir,
  buildId,
  keep = DEFAULT_STATIC_KEEP,
  log = console.log,
} = {}) {
  const archive = typeof archiveDir === 'string' ? archiveDir.trim() : ''
  if (!archive) return { skipped: true, kept: 0 }
  try {
    await stat(staticDir)
  } catch (error) {
    if (error && error.code === 'ENOENT') return { skipped: true, kept: 0 }
    throw error
  }

  const generationName = safeGenerationName(buildId)
  const generationDir = path.join(archive, generationName)
  await mkdir(generationDir, { recursive: true })
  await cp(staticDir, generationDir, { recursive: true, force: true })

  const generations = await listGenerations(archive)
  const retained = generations.slice(0, keepCount(keep))
  const retainedNames = new Set(retained.map(item => item.name))
  for (const generation of generations) {
    if (retainedNames.has(generation.name)) continue
    await rm(generation.dir, { recursive: true, force: true })
  }
  for (const generation of retained) {
    if (generation.name === generationName) continue
    await copyMissing(generation.dir, staticDir)
  }
  log(`[web-startup] retained ${retained.length} next static generations`)
  return { skipped: false, kept: retained.length }
}
