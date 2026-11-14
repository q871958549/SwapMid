import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import ts from 'typescript'
import { describe, expect, it } from 'vitest'

const USERS_PAGE = join(
  process.cwd(),
  'src/app/(dashboard)/account-management/users/page.tsx',
)
const LOGIN_PAGE = join(process.cwd(), 'src/app/(auth)/login/client.tsx')

function containsApiFetch(node: ts.Node): boolean {
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

describe('账号入口安全契约', () => {
  it('账号页主数据源不得 catch 后伪装为空数据或公开默认密码', () => {
    const text = readFileSync(USERS_PAGE, 'utf8')
    const source = ts.createSourceFile(
      USERS_PAGE,
      text,
      ts.ScriptTarget.Latest,
      true,
      ts.ScriptKind.TSX,
    )
    const fallbackLines: number[] = []

    const visit = (node: ts.Node) => {
      if (
        ts.isCallExpression(node)
        && ts.isPropertyAccessExpression(node.expression)
        && node.expression.name.text === 'catch'
        && containsApiFetch(node.expression.expression)
      ) {
        fallbackLines.push(
          source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1,
        )
      }
      ts.forEachChild(node, visit)
    }
    visit(source)

    expect(fallbackLines).toEqual([])
    expect(text).toContain('parseServerListPagination')
    expect(text).toContain('ACCOUNT_USER_LIST_MAX_PAGE_SIZE')
    expect(text).not.toContain('SwapMid@123456')
  })

  it('匿名登录页不得向未认证用户展示共享初始密码', () => {
    const text = readFileSync(LOGIN_PAGE, 'utf8')

    expect(text).not.toContain('SwapMid@123456')
    expect(text).not.toContain('firstLoginHint')
  })
})
