import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { TabListMenu } from './tab-list-menu'
import type { Tab } from '@/stores/use-tab-store'
import zhHansMessages from '../../../messages/zh-Hans.json'

// tab-list-menu.tsx 内部经 resolveTabTitle 调 useTranslations('Nav')（i18n 第二期批次2a）——
// 组件树需要 NextIntlClientProvider 才能渲染，否则抛 "No intl context found"。
// 用真实 zh-Hans messages 直供，保持与生产一致、免维护镜像消息表。
function renderWithIntl(ui: React.ReactElement) {
  return render(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

const tabs: Tab[] = [
  { key: '/dashboard', title: '仪表盘', path: '/dashboard' },
  { key: '/project-management/projects', title: '项目', path: '/project-management/projects' },
  { key: '/hr-service/courses', title: '课程管理', path: '/hr-service/courses' },
]

function setup(overrides: Partial<React.ComponentProps<typeof TabListMenu>> = {}) {
  const props = {
    tabs,
    activeKey: '/project-management/projects',
    onSelect: vi.fn(),
    onClose: vi.fn(),
    onCloseOthers: vi.fn(),
    onCloseAll: vi.fn(),
    ...overrides,
  }
  renderWithIntl(<TabListMenu {...props} />)
  return props
}

function openMenu() {
  fireEvent.click(screen.getByRole('button', { name: /全部页签/ }))
}

describe('TabListMenu', () => {
  it('触发按钮显示页签总数', () => {
    setup()
    expect(screen.getByRole('button', { name: '全部页签（3 个）' })).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
  })

  it('点开后列出全部页签,当前项带「当前」标记', () => {
    setup()
    openMenu()
    expect(screen.getByText('仪表盘')).toBeInTheDocument()
    expect(screen.getByText('项目')).toBeInTheDocument()
    expect(screen.getByText('课程管理')).toBeInTheDocument()
    expect(screen.getByText('当前')).toBeInTheDocument()
  })

  it('输入关键字过滤页签列表', () => {
    setup()
    openMenu()
    fireEvent.change(screen.getByPlaceholderText('搜索页签...'), { target: { value: '课程' } })
    expect(screen.getByText('课程管理')).toBeInTheDocument()
    expect(screen.queryByText('仪表盘')).not.toBeInTheDocument()
  })

  it('无匹配时显示空态文案', () => {
    setup()
    openMenu()
    fireEvent.change(screen.getByPlaceholderText('搜索页签...'), { target: { value: '不存在的页签' } })
    expect(screen.getByText('未找到匹配页签')).toBeInTheDocument()
  })

  it('点击页签项回调 onSelect 并关闭面板', async () => {
    const props = setup()
    openMenu()
    fireEvent.click(screen.getByText('课程管理'))
    expect(props.onSelect).toHaveBeenCalledWith(tabs[2])
    await waitFor(() => {
      expect(screen.queryByPlaceholderText('搜索页签...')).not.toBeInTheDocument()
    })
  })

  it('点击项内关闭按钮只回调 onClose,不触发 onSelect', () => {
    const props = setup()
    openMenu()
    fireEvent.click(screen.getByRole('button', { name: '关闭页签「课程管理」' }))
    expect(props.onClose).toHaveBeenCalledWith('/hr-service/courses')
    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('首页页签不渲染关闭按钮', () => {
    setup()
    openMenu()
    expect(screen.queryByRole('button', { name: '关闭页签「仪表盘」' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '关闭页签「项目」' })).toBeInTheDocument()
  })

  it('「关闭其他」「关闭全部」分别触发对应回调', () => {
    const props = setup()
    openMenu()
    fireEvent.click(screen.getByRole('button', { name: '关闭其他' }))
    expect(props.onCloseOthers).toHaveBeenCalledTimes(1)

    openMenu()
    fireEvent.click(screen.getByRole('button', { name: '关闭全部' }))
    expect(props.onCloseAll).toHaveBeenCalledTimes(1)
  })

  it('除首页与当前页外无其他页签时,「关闭其他」禁用', () => {
    setup({
      tabs: [tabs[0], tabs[1]],
      activeKey: tabs[1].key,
    })
    openMenu()
    expect(screen.getByRole('button', { name: '关闭其他' })).toBeDisabled()
    expect(screen.getByRole('button', { name: '关闭全部' })).toBeEnabled()
  })

  it('只剩首页时,「关闭其他」与「关闭全部」都禁用', () => {
    setup({ tabs: [tabs[0]], activeKey: tabs[0].key })
    openMenu()
    expect(screen.getByRole('button', { name: '关闭其他' })).toBeDisabled()
    expect(screen.getByRole('button', { name: '关闭全部' })).toBeDisabled()
  })
})
