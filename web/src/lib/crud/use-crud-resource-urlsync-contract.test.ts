import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const APP_ROOT = join(process.cwd(), 'src', 'app')

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

function isCommentedCall(text: string, index: number): boolean {
  const lineStart = text.lastIndexOf('\n', index - 1) + 1
  const line = text.slice(lineStart, text.indexOf('\n', index))
  const trimmed = line.trimStart()
  return trimmed.startsWith('//') || trimmed.startsWith('*')
}

/**
 * 顶栏列表必须显式 urlSync: true；同页第二份 / 抽屉内列表必须显式 urlSync: false。
 * 默认 false 只为次列表不污染地址栏——新年漏写会静默丢「切走再切回」的视图。
 */
describe('useCrudResource urlSync 门禁', () => {
  it('每个真实调用都必须显式写 urlSync: true 或 urlSync: false', () => {
    const violations: string[] = []

    for (const file of walk(APP_ROOT)) {
      const text = readFileSync(file, 'utf8')
      if (!text.includes('useCrudResource')) continue

      const re = /useCrudResource(?:<[^>\n]+>)?\s*\(/g
      let match: RegExpExecArray | null
      while ((match = re.exec(text))) {
        if (isCommentedCall(text, match.index)) continue

        const paren = match.index + match[0].length - 1
        const end = indexOfMatching(text, paren, '(', ')')
        if (end < 0) continue
        const args = text.slice(paren, end + 1)
        if (!args.includes('{')) continue

        if (!/urlSync\s*:\s*(true|false)/.test(args)) {
          const rel = relative(process.cwd(), file).replaceAll('\\', '/')
          const line = text.slice(0, match.index).split('\n').length
          violations.push(`${rel}:${line}`)
        }
      }
    }

    expect(violations, violations.join('\n')).toEqual([])
  })
})
