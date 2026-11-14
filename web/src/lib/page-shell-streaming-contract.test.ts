import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const DASHBOARD_ROOT = join(process.cwd(), 'src', 'app', '(dashboard)')

function pageFiles(root: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name)
    if (entry.isDirectory()) files.push(...pageFiles(path))
    else if (entry.name === 'page.tsx') files.push(path)
  }
  return files
}

function indexOfMatching(text: string, start: number, open: string, close: string): number {
  let depth = 0
  for (let i = start; i < text.length; i++) {
    if (text[i] === open) depth++
    else if (text[i] === close) {
      depth--
      if (depth === 0) return i
    }
  }
  return -1
}

/** 取出默认导出函数体。必须先越过参数列表，否则会把 `{ searchParams: Promise<...> }` 当成函数体。 */
function extractDefaultExport(text: string): string | null {
  const match = text.match(/^export default async function \w+/m)
  if (!match || match.index == null) return null

  let i = match.index + match[0].length
  while (i < text.length && /\s/.test(text[i]!)) i++
  if (text[i] === '<') {
    const genericEnd = indexOfMatching(text, i, '<', '>')
    if (genericEnd < 0) return null
    i = genericEnd + 1
    while (i < text.length && /\s/.test(text[i]!)) i++
  }
  if (text[i] !== '(') return null

  const parenEnd = indexOfMatching(text, i, '(', ')')
  if (parenEnd < 0) return null
  const braceStart = text.indexOf('{', parenEnd)
  if (braceStart < 0) return null
  const braceEnd = indexOfMatching(text, braceStart, '{', '}')
  if (braceEnd < 0) return null
  return text.slice(match.index, braceEnd + 1)
}

describe('后台页流式外壳门禁', () => {
  it('默认导出不得直接 apiFetch / getSession，取数必须落在 Suspense 内层', () => {
    const violations: string[] = []

    for (const file of pageFiles(DASHBOARD_ROOT)) {
      const text = readFileSync(file, 'utf8')
      const rel = relative(process.cwd(), file).replaceAll('\\', '/')
      const needsStream = /\bapiFetch\s*[<(]/.test(text) || /\bgetSession\s*\(/.test(text)
      if (!needsStream) continue

      const defaultExport = extractDefaultExport(text)
      if (!defaultExport) {
        violations.push(`${rel}: 找不到 export default async function`)
        continue
      }
      if (!/<Suspense[\s>]/.test(defaultExport)) {
        violations.push(`${rel}: 有取数但默认导出未包 <Suspense>`)
        continue
      }
      if (/\bapiFetch\s*[<(]/.test(defaultExport) || /\bgetSession\s*\(/.test(defaultExport)) {
        violations.push(`${rel}: 默认导出仍直接取数，外壳会被查询堵住`)
      }
    }

    expect(violations, violations.join('\n')).toEqual([])
  })
})
