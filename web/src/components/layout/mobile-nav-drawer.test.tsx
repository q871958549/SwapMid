import { describe, it, expect, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../messages/zh-Hans.json'

let mockPathname = '/data-integration/pipelines'
vi.mock('next/navigation', () => ({
  usePathname: () => mockPathname,
  useRouter: () => ({ push: vi.fn() }),
}))
vi.mock('next/link', () => ({
  default: ({ href, children, prefetch, ...props }: React.ComponentProps<'a'> & { prefetch?: boolean | null }) => (
    <a {...props} href={typeof href === 'string' ? href : undefined} data-prefetch={String(prefetch)}>{children}</a>
  ),
}))
vi.mock('next/image', () => ({
  default: ({ src, alt, unoptimized, ...props }: React.ComponentProps<'img'> & { unoptimized?: boolean }) => (
    // eslint-disable-next-line @next/next/no-img-element
    <img src={typeof src === 'string' ? src : ''} alt={alt} data-unoptimized={String(unoptimized)} {...props} />
  ),
}))

import { MobileNavDrawer } from './mobile-nav-drawer'

const grantedPolicies = {
  'SwapMid.DataIntegration.DataSources': true,
  'SwapMid.DataIntegration.DataPipelines': true,
}

function renderDrawer() {
  return render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <MobileNavDrawer grantedPolicies={grantedPolicies} />
    </NextIntlClientProvider>,
  )
}

function openDrawer() {
  fireEvent.click(screen.getByRole('button', { name: '打开导航菜单' }))
  return screen.getByRole('dialog')
}

describe('MobileNavDrawer', () => {
  beforeEach(() => {
    mockPathname = '/data-integration/pipelines'
  })

  it('常态只渲染汉堡钮，抽屉内容不挂载', () => {
    renderDrawer()
    const trigger = screen.getByRole('button', { name: '打开导航菜单' })
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByText('SwapMid')).not.toBeInTheDocument()
  })

  it('点汉堡打开抽屉：装的是展开态侧栏，跟随当前页所在组，且没有桌面的折叠钮', () => {
    renderDrawer()
    const dialog = openDrawer()

    expect(dialog).toHaveTextContent('SwapMid')
    expect(screen.getByText('导航菜单')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /数据源/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /数据管道/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '关闭导航菜单' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '收起导航栏' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '打开导航菜单', hidden: true })).toHaveAttribute('aria-expanded', 'true')
  })

  it('点品牌行关闭钮收起抽屉', async () => {
    renderDrawer()
    openDrawer()
    fireEvent.click(screen.getByRole('button', { name: '关闭导航菜单' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('点导航项即收起抽屉', async () => {
    renderDrawer()
    openDrawer()
    fireEvent.click(screen.getByRole('link', { name: /数据源/ }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('路由变化时自动收起', async () => {
    const view = renderDrawer()
    openDrawer()

    mockPathname = '/dashboard'
    view.rerender(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <MobileNavDrawer grantedPolicies={grantedPolicies} />
      </NextIntlClientProvider>,
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})
