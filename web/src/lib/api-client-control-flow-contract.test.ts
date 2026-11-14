import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import ts from 'typescript'
import { describe, expect, it } from 'vitest'

const APP_ROOT = join(process.cwd(), 'src', 'app')
const ARCHITECTURE_SCAN_TIMEOUT_MS = 15_000

function sourceFiles(root: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name)
    if (entry.isDirectory()) files.push(...sourceFiles(path))
    else if (/\.(?:ts|tsx)$/.test(entry.name)) files.push(path)
  }
  return files
}

function containsApiFetchCall(node: ts.Node): boolean {
  let found = false
  const visit = (current: ts.Node) => {
    if (
      ts.isCallExpression(current)
      && ts.isIdentifier(current.expression)
      && current.expression.text === 'apiFetch'
    ) {
      found = true
      return
    }
    if (!found) ts.forEachChild(current, visit)
  }
  visit(node)
  return found
}

function containsAwaitedApiFetch(node: ts.Node): boolean {
  let found = false
  const visit = (current: ts.Node) => {
    if (ts.isAwaitExpression(current) && containsApiFetchCall(current.expression)) {
      found = true
      return
    }
    if (!found) ts.forEachChild(current, visit)
  }
  visit(node)
  return found
}

function lineOf(source: ts.SourceFile, node: ts.Node): number {
  return source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1
}

describe('apiFetch Next 控制流契约', () => {
  it('fallback 不得吞 redirect/notFound，且不绕开受保护的 catch 链', () => {
    const violations: string[] = []

    for (const file of sourceFiles(APP_ROOT)) {
      const source = ts.createSourceFile(
        file,
        readFileSync(file, 'utf8'),
        ts.ScriptTarget.Latest,
        true,
      )
      const display = relative(process.cwd(), file).replaceAll('\\', '/')

      const visit = (node: ts.Node) => {
        if (
          ts.isTryStatement(node)
          && containsAwaitedApiFetch(node.tryBlock)
          && (
            !node.catchClause
            || !node.catchClause.block
              .getText(source)
              .includes('rethrowApiFetchControlFlow')
          )
        ) {
          violations.push(
            `${display}:${lineOf(source, node)} await apiFetch 的 catch 缺少 rethrowApiFetchControlFlow`,
          )
        }

        if (
          ts.isCallExpression(node)
          && ts.isPropertyAccessExpression(node.expression)
          && node.expression.name.text === 'then'
          && node.arguments.length > 1
          && containsApiFetchCall(node.expression.expression)
        ) {
          violations.push(
            `${display}:${lineOf(source, node)} apiFetch.then 禁止第二个 rejection 参数`,
          )
        }

        if (
          ts.isCallExpression(node)
          && ts.isPropertyAccessExpression(node.expression)
          && node.expression.name.text === 'catch'
          && ts.isCallExpression(node.expression.expression)
          && ts.isPropertyAccessExpression(
            node.expression.expression.expression,
          )
          && node.expression.expression.expression.name.text === 'all'
          && containsApiFetchCall(node.expression.expression)
        ) {
          violations.push(
            `${display}:${lineOf(source, node)} Promise.all(apiFetch) 外层 catch 会绕开控制流保护`,
          )
        }

        ts.forEachChild(node, visit)
      }
      visit(source)
    }

    expect(violations).toEqual([])
  }, ARCHITECTURE_SCAN_TIMEOUT_MS)
})
