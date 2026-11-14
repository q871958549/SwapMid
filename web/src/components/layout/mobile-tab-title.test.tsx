import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { NextIntlClientProvider, useTranslations } from 'next-intl'
import type { NavT } from '@/lib/nav-utils'
import type { Tab } from '@/stores/use-tab-store'
import { MobileTabTitle } from './mobile-tab-title'
import zhHansMessages from '../../../messages/zh-Hans.json'

const tabs: Tab[] = [
  { key: '/dashboard', title: '仪表盘', path: '/dashboard' },
  { key: '/project-management/projects', title: '项目', path: '/project-management/projects' },
  { key: '/project-management/samples/1', title: '样品详情 · SN-001', path: '/project-management/samples/1' },
]

function Harness({ activeKey, list = tabs }: { activeKey: string; list?: Tab[] }) {
  const t = useTranslations('Nav') as NavT
  return <MobileTabTitle tabs={list} activeKey={activeKey} t={t} />
}

function renderTitle(activeKey: string, list?: Tab[]) {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <Harness activeKey={activeKey} list={list} />
    </NextIntlClientProvider>,
  )
}

describe('MobileTabTitle', () => {
  it('只显示当前页签的标题', () => {
    renderTitle('/project-management/projects')
    expect(screen.getByText('项目')).toBeInTheDocument()
    expect(screen.queryByText('仪表盘')).not.toBeInTheDocument()
  })

  it('详情页由业务数据覆盖过的标题原样显示（与桌面页签同一 resolveTabTitle）', () => {
    renderTitle('/project-management/samples/1')
    expect(screen.getByText('样品详情 · SN-001')).toBeInTheDocument()
  })

  it('首页按当前语言现场求值', () => {
    renderTitle('/dashboard')
    expect(screen.getByText('仪表盘')).toBeInTheDocument()
  })

  it('activeKey 不在页签里（水合前/关闭竞态）时回退到首页，不留空白', () => {
    renderTitle('/not-opened')
    expect(screen.getByText('仪表盘')).toBeInTheDocument()
  })

  it('没有任何页签时不渲染', () => {
    const { container } = renderTitle('/dashboard', [])
    expect(container).toBeEmptyDOMElement()
  })
})
