import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import type { NavT } from '@/lib/nav-utils'
import { SidebarFooter } from './sidebar'
import zhHansMessages from '../../../messages/zh-Hans.json'

const ts = ((key: string) => {
  const texts: Record<string, string> = {
    expandNav: '展开导航栏',
    collapseNav: '收起导航栏',
    publishDefault: '设为全员默认',
    publishDefaultAction: '发布',
    publishDefaultDescription: '把你当前的导航排序设为全员默认。',
  }
  return texts[key] ?? key
}) as NavT

const release = {
  text: '20260914 · 2c6583db',
  shortText: '20260914',
  title: '20260914-011816-2c6583db79ba',
}

function renderFooter(ui: React.ReactElement) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      {ui}
    </NextIntlClientProvider>,
  )
}

describe('SidebarFooter', () => {
  it('展开态左侧常显日期、右侧只留恢复图标，完整版本号与恢复文案不常显', () => {
    renderFooter(
      <SidebarFooter
        collapsed={false}
        isCustomized
        release={release}
        ts={ts}
        onToggleCollapsed={() => {}}
        resetLabel="恢复本域默认排序"
        onResetOrder={() => {}}
      />,
    )

    expect(screen.getByText('20260914')).toBeInTheDocument()
    expect(screen.queryByText(release.text)).not.toBeInTheDocument()
    expect(screen.getByLabelText(release.text)).toBeInTheDocument()
    expect(screen.queryByText('恢复本域默认排序')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '恢复本域默认排序' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '收起导航栏' })).toBeInTheDocument()
    expect(screen.getByText('20260914').closest('button')).toBeNull()
  })

  it('抽屉形态（showCollapseToggle=false）隐去折叠钮，日期与重置仍在', () => {
    renderFooter(
      <SidebarFooter
        collapsed={false}
        isCustomized
        release={release}
        ts={ts}
        showCollapseToggle={false}
        onToggleCollapsed={() => {}}
        resetLabel="恢复本域默认排序"
        onResetOrder={() => {}}
      />,
    )

    expect(screen.queryByRole('button', { name: '收起导航栏' })).not.toBeInTheDocument()
    expect(screen.getByText('20260914')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '恢复本域默认排序' })).toBeInTheDocument()
  })

  it('未自定义排序时不渲染恢复图标，日期仍在', () => {
    renderFooter(
      <SidebarFooter
        collapsed={false}
        isCustomized={false}
        release={release}
        ts={ts}
        onToggleCollapsed={() => {}}
        resetLabel="恢复本域默认排序"
        onResetOrder={() => {}}
      />,
    )

    expect(screen.getByText('20260914')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '恢复本域默认排序' })).not.toBeInTheDocument()
  })

  it('点击恢复立即生效、不弹确认框（撤销由调用方的 toast 提供）', () => {
    const onResetOrder = vi.fn()
    renderFooter(
      <SidebarFooter
        collapsed={false}
        isCustomized
        release={release}
        ts={ts}
        onToggleCollapsed={() => {}}
        resetLabel="恢复本域默认排序"
        onResetOrder={onResetOrder}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: '恢复本域默认排序' }))
    expect(onResetOrder).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('非 admin（不传 onPublishDefault）没有「设为全员默认」', () => {
    renderFooter(
      <SidebarFooter
        collapsed={false}
        isCustomized
        release={release}
        ts={ts}
        onToggleCollapsed={() => {}}
        resetLabel="恢复本域默认排序"
        onResetOrder={() => {}}
      />,
    )

    expect(screen.queryByRole('button', { name: '设为全员默认' })).not.toBeInTheDocument()
  })

  it('admin 的「设为全员默认」与是否自定义无关，且要确认后才发布', async () => {
    const onPublishDefault = vi.fn()
    renderFooter(
      <SidebarFooter
        collapsed={false}
        isCustomized={false}
        release={release}
        ts={ts}
        onToggleCollapsed={() => {}}
        resetLabel="恢复本域默认排序"
        onResetOrder={() => {}}
        onPublishDefault={onPublishDefault}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: '设为全员默认' }))
    expect(onPublishDefault).not.toHaveBeenCalled()
    fireEvent.click(await screen.findByRole('button', { name: '发布' }))
    expect(onPublishDefault).toHaveBeenCalledTimes(1)
  })
})
