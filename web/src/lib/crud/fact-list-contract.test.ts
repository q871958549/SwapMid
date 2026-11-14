import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'
import { FACT_LIST_PAGES, type FactListExportKind } from './fact-list-pages'

const WEB_ROOT = process.cwd()
const APP_ROOT = join(WEB_ROOT, 'src', 'app', '(dashboard)')

function walk(dir: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) files.push(...walk(path))
    else if (/\.(tsx|ts)$/.test(entry.name) && !/\.test\.(tsx|ts)$/.test(entry.name)) {
      files.push(path)
    }
  }
  return files
}

function rel(path: string): string {
  return relative(WEB_ROOT, path).replaceAll('\\', '/')
}

function evidenceOf(kind: FactListExportKind, text: string): boolean {
  switch (kind) {
    case 'server':
      return /serverExportPath\s*:/.test(text)
    case 'disabled':
      return /onExportAll\s*:\s*undefined/.test(text)
    case 'capped':
      return /countCap\s*=/.test(text) || /exportMaxRows\s*:\s*\d+/.test(text) ||
        /exportMaxRows\s*:\s*DEFAULT_EXPORT_MAX_ROWS/.test(text)
    default:
      return false
  }
}

describe('事实表列表菜谱门禁', () => {
  it('登记表里的每一页都在，且导出策略与源码一致', () => {
    const violations: string[] = []

    for (const entry of FACT_LIST_PAGES) {
      const abs = join(WEB_ROOT, entry.file)
      if (!existsSync(abs)) {
        violations.push(`缺失文件 ${entry.file}`)
        continue
      }
      const text = readFileSync(abs, 'utf8')
      const code = text.replace(/\/\/[^\n]*/g, '')
      if (/exportMaxRows\s*:\s*null/.test(code)) {
        violations.push(`${entry.file}：事实表禁止 exportMaxRows: null`)
      }
      if (!evidenceOf(entry.export, code)) {
        violations.push(`${entry.file}：声明 export=${entry.export}，源码对不上`)
      }
    }

    expect(violations, violations.join('\n')).toEqual([])
  })

  it('写了 serverExportPath 或 countCap 的页面必须进登记表', () => {
    const registered = new Set(FACT_LIST_PAGES.map((e) => e.file))
    const orphans: string[] = []

    for (const file of walk(APP_ROOT)) {
      const text = readFileSync(file, 'utf8')
      const isFactSignal = /serverExportPath\s*:/.test(text) || /countCap\s*=/.test(text)
      if (!isFactSignal) continue
      const key = rel(file)
      if (!registered.has(key)) orphans.push(key)
    }

    expect(orphans, orphans.join('\n')).toEqual([])
  })
})
