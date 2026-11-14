import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from './data-table'
import { PermissionsProvider } from '@/components/providers/permissions-provider'
import zhHansMessages from '../../../messages/zh-Hans.json'

const path = vi.hoisted(() => ({ value: '/data-integration/pipelines' }))
vi.mock('next/navigation', () => ({ usePathname: () => path.value }))

interface Row { name: string }

const columns: ColumnDef<Row, unknown>[] = [{ accessorKey: 'name', header: '名称' }]
const rows: Row[] = [{ name: '订单同步管道' }]

const PIPELINES_EXPORT = 'SwapMid.DataIntegration.DataPipelines.Export'
const SOURCES_EXPORT = 'SwapMid.DataIntegration.DataSources.Export'

function renderTable(policies: Record<string, boolean>) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <PermissionsProvider value={policies}>
        <DataTable columns={columns} data={rows} tableKey={`perm-${path.value}`} />
      </PermissionsProvider>
    </NextIntlClientProvider>,
  )
}

beforeEach(() => {
  localStorage.clear()
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
})

describe('DataTable 导出权限按当前路由导航项精确匹配', () => {
  it('数据管道页只持有数据源导出权限时，不显示表格导出', async () => {
    path.value = '/data-integration/pipelines'
    renderTable({ [SOURCES_EXPORT]: true })
    await screen.findByRole('button', { name: '列' })
    expect(screen.queryByRole('button', { name: '导出' })).not.toBeInTheDocument()
  })

  it('数据管道页持有本资源导出权限时，显示表格导出', async () => {
    path.value = '/data-integration/pipelines'
    renderTable({ [PIPELINES_EXPORT]: true })
    await screen.findByRole('button', { name: '列' })
    expect(screen.getByRole('button', { name: '导出' })).toBeInTheDocument()
  })
})
