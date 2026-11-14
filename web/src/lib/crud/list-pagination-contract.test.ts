import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const APP_ROOT = join(process.cwd(), 'src', 'app')
// 2026-07-28 基线：61 个存量列表页各有 page/size 两处直转。
// 新页面必须使用 parseServerListPagination；存量迁移时只允许该数字下降。
const LEGACY_DIRECT_PAGINATION_PARSE_MAX = 122

function pageFiles(root: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name)
    if (entry.isDirectory()) files.push(...pageFiles(path))
    else if (entry.name === 'page.tsx') files.push(path)
  }
  return files
}

const DIRECT_PARSE = /Number\(sp\.(?:page|size)\)/g

function locate(file: string, text: string): string[] {
  const lines: string[] = []
  let line = 1
  let cursor = 0
  DIRECT_PARSE.lastIndex = 0
  for (const match of text.matchAll(DIRECT_PARSE)) {
    const index = match.index ?? 0
    while (cursor < index) {
      if (text.charCodeAt(cursor) === 10) line += 1
      cursor += 1
    }
    lines.push(`${file}:${line}`)
  }
  return lines
}

describe('服务端列表分页解析迁移门禁', () => {
  // 成功路径只计数。行号在超基线时才算，避免每个匹配都 slice+split 整文件。
  // 自托管 runner 高并发时，遍历全部 page.tsx 仍可能撞 vitest 默认 5 秒；这里只放宽这一次扫描。
  it('直接 Number(sp.page/size) 的存量基线只减不增', () => {
    let count = 0
    const pending: { file: string; text: string }[] = []
    for (const path of pageFiles(APP_ROOT)) {
      const text = readFileSync(path, 'utf8')
      const matches = text.match(DIRECT_PARSE)
      if (!matches?.length) continue
      count += matches.length
      pending.push({ file: relative(process.cwd(), path).replaceAll('\\', '/'), text })
    }
    const detail = count > LEGACY_DIRECT_PAGINATION_PARSE_MAX
      ? pending.flatMap(item => locate(item.file, item.text)).join('\n')
      : ''
    expect(count, detail).toBeLessThanOrEqual(LEGACY_DIRECT_PAGINATION_PARSE_MAX)
  }, 15_000)
})
