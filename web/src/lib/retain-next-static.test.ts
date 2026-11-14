import { mkdir, mkdtemp, readFile, utimes, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { describe, expect, it } from 'vitest'
import { retainNextStaticGenerations } from '../../scripts/retain-next-static.mjs'

const retain = retainNextStaticGenerations as unknown as (options: {
  staticDir: string
  archiveDir: string
  buildId: string
  keep: number
  log?: () => void
}) => Promise<{ skipped: boolean; kept: number }>

async function writeGeneration(root: string, name: string, fileName: string, body: string) {
  const dir = path.join(root, name)
  await mkdir(dir, { recursive: true })
  await writeFile(path.join(dir, fileName), body)
}

describe('retainNextStaticGenerations', () => {
  it('保留最近几代，并把旧 chunk 铺回当前静态目录', async () => {
    const root = await mkdtemp(path.join(tmpdir(), 'next-static-'))
    const staticDir = path.join(root, 'static')
    const archiveDir = path.join(root, 'archive')
    await writeGeneration(staticDir, '.', 'current.js', 'new')
    await writeGeneration(archiveDir, 'build-old', 'old.js', 'old')
    await writeGeneration(archiveDir, 'build-older', 'older.js', 'older')
    await utimes(path.join(archiveDir, 'build-older'), new Date(1_000), new Date(1_000))
    await utimes(path.join(archiveDir, 'build-old'), new Date(2_000), new Date(2_000))

    const result = await retain({
      staticDir,
      archiveDir,
      buildId: 'build-new',
      keep: 2,
      log: () => undefined,
    })

    expect(result.skipped).toBe(false)
    expect(result.kept).toBe(2)
    expect(await readFile(path.join(staticDir, 'current.js'), 'utf8')).toBe('new')
    expect(await readFile(path.join(staticDir, 'old.js'), 'utf8')).toBe('old')
    await expect(readFile(path.join(staticDir, 'older.js'), 'utf8')).rejects.toThrow()
    expect(await readFile(path.join(archiveDir, 'build-new', 'current.js'), 'utf8')).toBe('new')
  })

  it('未配置归档目录时跳过', async () => {
    const result = await retain({
      staticDir: '/missing',
      archiveDir: '  ',
      buildId: 'build-new',
      keep: 5,
    })
    expect(result).toEqual({ skipped: true, kept: 0 })
  })
})
