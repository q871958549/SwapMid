import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { navGroups } from '@/components/layout/nav-config'
import { Permissions } from './permissions'

function loadBackendDefinedPermissions(): Set<string> {
  const permissionsPath = join(
    process.cwd(),
    '..',
    'src',
    'SwapMid.Application.Contracts',
    'Permissions',
    'SwapMidPermissions.cs',
  )
  const providerPath = join(
    process.cwd(),
    '..',
    'src',
    'SwapMid.Application.Contracts',
    'Permissions',
    'SwapMidPermissionDefinitionProvider.cs',
  )

  const permCs = readFileSync(permissionsPath, 'utf-8')
  const providerCs = readFileSync(providerPath, 'utf-8')

  const defined = new Set<string>(['SwapMid.EmployeeSelfService'])
  let currentDefault: string | null = null

  for (const line of permCs.split(/\r?\n/)) {
    const defMatch = /const string Default = GroupName \+ "([^"]+)"/.exec(line)
    if (defMatch) {
      currentDefault = `SwapMid${defMatch[1]}`
      defined.add(currentDefault)
      continue
    }
    const childMatch = /const string \w+ = Default \+ "([^"]+)"/.exec(line)
    if (childMatch && currentDefault) {
      defined.add(`${currentDefault}${childMatch[1]}`)
    }
  }

  const dataIoRegex = /var (\w+) = \w+\.AddPermission\(\s*SwapMidPermissions\.(\w+)\.(\w+)\.Default/g
  for (const match of providerCs.matchAll(dataIoRegex)) {
    const [, varName, domain, resource] = match
    if (providerCs.includes(`AddDataIo(${varName}`)) {
      defined.add(`SwapMid.${domain}.${resource}.Export`)
      defined.add(`SwapMid.${domain}.${resource}.Import`)
    }
  }

  return defined
}

describe('前端权限常量与导航守卫同后端 SwapMidPermissions 一致性契约', () => {
  const backendPermissions = loadBackendDefinedPermissions()

  it('Permissions 对象中的每一项都存在于后端 SwapMidPermissions', () => {
    const allFrontendConstants = Object.entries(Permissions).flatMap(([group, entries]) =>
      Object.entries(entries).map(([key, value]) => ({ ref: `Permissions.${group}.${key}`, value })),
    )

    const missing = allFrontendConstants.filter((entry) => !backendPermissions.has(entry.value))
    expect(missing).toEqual([])
  })

  it('侧栏导航 navGroups 挂载的每一个 permission 都存在于后端 SwapMidPermissions', () => {
    const navPermissionEntries = navGroups.flatMap((group) =>
      group.items
        .filter((item) => Boolean(item.permission))
        .map((item) => ({ href: item.href, permission: item.permission! })),
    )

    const missing = navPermissionEntries.filter((entry) => !backendPermissions.has(entry.permission))
    expect(missing).toEqual([])
  })
})
