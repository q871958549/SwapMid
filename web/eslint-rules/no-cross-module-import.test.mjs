// @vitest-environment node

import { Linter } from 'eslint'
import { describe, expect, it } from 'vitest'
import plugin from './no-cross-module-import.mjs'

const ruleName = 'swapmid/no-cross-module-import'
const accountFile =
  'src/app/(dashboard)/account-management/users/service.ts'

function lint(code, filename = accountFile) {
  const linter = new Linter({ configType: 'flat' })
  return linter.verify(
    code,
    {
      files: ['**/*.{ts,tsx}'],
      languageOptions: {
        ecmaVersion: 'latest',
        sourceType: 'module',
      },
      plugins: { swapmid: plugin },
      rules: { [ruleName]: 'error' },
    },
    { filename },
  )
}

describe('swapmid/no-cross-module-import', () => {
  it.each([
    ['alias', "import { EmployeeTable } from '@/app/(dashboard)/enterprise-management/employees/client'"],
    ['relative path', "import { EmployeeTable } from '../../enterprise-management/employees/client'"],
    ['normalized alias', "import { EmployeeTable } from '@/app/(dashboard)/account-management/../enterprise-management/employees/client'"],
    ['named re-export', "export { EmployeeTable } from '@/app/(dashboard)/enterprise-management/employees/client'"],
    ['star re-export', "export * from '../../enterprise-management/employees/client'"],
    ['dynamic import', "const module = import('../../enterprise-management/employees/client')"],
  ])('rejects cross-module imports through %s', (_, code) => {
    expect(lint(code)).toMatchObject([
      {
        ruleId: ruleName,
        messageId: 'crossModule',
        severity: 2,
      },
    ])
  })

  it.each([
    ['same-module alias', "import { Role } from '@/app/(dashboard)/account-management/roles/shared'"],
    ['same-module relative path', "import { Role } from '../roles/shared'"],
    ['shared alias', "import { Button } from '@/components/ui/button'"],
    ['shared relative path', "import { Button } from '../../../../components/ui/button'"],
  ])('allows %s imports', (_, code) => {
    expect(lint(code)).toEqual([])
  })

  it('does not enforce a business boundary for files in the shared layer', () => {
    const messages = lint(
      "import { EmployeeTable } from '@/app/(dashboard)/enterprise-management/employees/client'",
      'src/components/employee-table.ts',
    )

    expect(messages).toEqual([])
  })
})
