import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import ts from 'typescript'
import { describe, expect, it } from 'vitest'

const SRC_ROOT = join(process.cwd(), 'src')
const ARCHITECTURE_SCAN_TIMEOUT_MS = 15_000

const DIRECT_STORAGE_ALLOWLIST = new Set([
  'src/lib/browser-storage-policy.ts',
  'src/components/theme-provider.tsx',
  'src/components/data-table/use-table-persistence.ts',
  'src/components/common/department-picker.tsx',
  'src/app/(auth)/login/client.tsx',
  'src/app/layout.tsx',
])

const LEGACY_AUTHENTICATED_KEYS = new Set([
  'swapmid-api-console-history',
  'swapmid-recent-visits',
  'swapmid-tabs',
  'swapmid-scroll',
])

const LEGACY_KEY_ALLOWLIST = new Set([
  'src/lib/browser-storage-policy.ts',
])

function sourceFiles(root: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name)
    if (entry.isDirectory()) {
      files.push(...sourceFiles(path))
      continue
    }
    if (!/\.(?:ts|tsx)$/.test(entry.name)) continue
    if (/\.test\.(?:ts|tsx)$/.test(entry.name) || entry.name.endsWith('.d.ts')) continue
    files.push(path)
  }
  return files
}

function displayPath(file: string): string {
  return relative(process.cwd(), file).replaceAll('\\', '/')
}

function lineOf(source: ts.SourceFile, node: ts.Node): number {
  return source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1
}

function isBrowserStorageIdentifier(node: ts.Node): boolean {
  return ts.isIdentifier(node)
    && (node.text === 'localStorage' || node.text === 'sessionStorage')
}

function isBrowserStorageElementAccess(node: ts.Node): boolean {
  if (!ts.isElementAccessExpression(node)) return false
  const argument = node.argumentExpression
  return ts.isStringLiteral(argument)
    && (argument.text === 'localStorage' || argument.text === 'sessionStorage')
}

describe('浏览器持久化架构契约', () => {
  it('业务代码不得绕过集中策略直接访问 localStorage/sessionStorage', () => {
    const violations: string[] = []

    for (const file of sourceFiles(SRC_ROOT)) {
      const display = displayPath(file)
      if (DIRECT_STORAGE_ALLOWLIST.has(display)) continue

      const source = ts.createSourceFile(
        file,
        readFileSync(file, 'utf8'),
        ts.ScriptTarget.Latest,
        true,
      )

      const visit = (node: ts.Node) => {
        if (isBrowserStorageIdentifier(node) || isBrowserStorageElementAccess(node)) {
          violations.push(
            `${display}:${lineOf(source, node)} 必须通过 browser-storage-policy 访问浏览器存储`,
          )
          return
        }
        ts.forEachChild(node, visit)
      }
      visit(source)
    }

    expect(violations).toEqual([])
  }, ARCHITECTURE_SCAN_TIMEOUT_MS)

  it('旧无作用域业务键只能出现在集中清理注册表或说明文档中', () => {
    const violations: string[] = []

    for (const file of sourceFiles(SRC_ROOT)) {
      const display = displayPath(file)
      if (LEGACY_KEY_ALLOWLIST.has(display)) continue

      const source = ts.createSourceFile(
        file,
        readFileSync(file, 'utf8'),
        ts.ScriptTarget.Latest,
        true,
      )

      const visit = (node: ts.Node) => {
        if (
          (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node))
          && LEGACY_AUTHENTICATED_KEYS.has(node.text)
        ) {
          violations.push(
            `${display}:${lineOf(source, node)} 禁止重新引入旧无作用域键 ${node.text}`,
          )
        }
        ts.forEachChild(node, visit)
      }
      visit(source)
    }

    expect(violations).toEqual([])
  }, ARCHITECTURE_SCAN_TIMEOUT_MS)
})
