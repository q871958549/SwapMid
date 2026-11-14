import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import ts from 'typescript'
import { describe, expect, it } from 'vitest'

const APP_ROOT = join(process.cwd(), 'src', 'app')

function pageFiles(root: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name)
    if (entry.isDirectory()) files.push(...pageFiles(path))
    else if (entry.name === 'page.tsx') files.push(path)
  }
  return files
}

function unwrap(node: ts.Expression): ts.Expression {
  let current = node
  while (true) {
    if (ts.isParenthesizedExpression(current)) {
      current = current.expression
      continue
    }
    if (ts.isAwaitExpression(current)) {
      current = current.expression
      continue
    }
    return current
  }
}

function isCookiesCall(node: ts.Expression): boolean {
  const target = unwrap(node)
  return ts.isCallExpression(target)
    && ts.isIdentifier(target.expression)
    && target.expression.text === 'cookies'
}

function isCookieStoreIdentifier(node: ts.Expression): boolean {
  const target = unwrap(node)
  return ts.isIdentifier(target) && /cookie/i.test(target.text)
}

function cookieMutations(file: string): string[] {
  const text = readFileSync(file, 'utf8')
  const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
  const rel = relative(process.cwd(), file).replaceAll('\\', '/')
  const found: string[] = []

  const visit = (node: ts.Node) => {
    if (
      ts.isCallExpression(node)
      && ts.isPropertyAccessExpression(node.expression)
      && (node.expression.name.text === 'set' || node.expression.name.text === 'delete')
    ) {
      const target = node.expression.expression
      if (isCookiesCall(target) || isCookieStoreIdentifier(target)) {
        const line = source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1
        found.push(`${rel}:${line} ${node.expression.name.text}`)
      }
    }
    ts.forEachChild(node, visit)
  }

  visit(source)
  return found
}

describe('页面不得改 Cookie', () => {
  it('app/**/page.tsx 禁止 cookies().set / delete，避免 Next 16 渲染期 500', () => {
    const violations = pageFiles(APP_ROOT).flatMap(cookieMutations)
    expect(violations, violations.join('\n')).toEqual([])
  })
})
