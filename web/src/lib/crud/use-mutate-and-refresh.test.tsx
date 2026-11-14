import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook as renderHookRTL } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { toast } from '@/components/ui/use-toast'
import { useMutateAndRefresh } from './use-mutate-and-refresh'
import zhHansMessages from '../../../messages/zh-Hans.json'

// useMutateAndRefresh 内失败 toast 的默认文案经 useTranslations('Common') 求值（i18n 批次4b前置），
// 组件树需要 NextIntlClientProvider 才能渲染；真实 zh-Hans messages 直供，与生产一致。
// 用同名遮蔽包一层 wrapper，断言零改动。
function renderHook<TProps, TResult>(
  callback: (props: TProps) => TResult,
  options?: Parameters<typeof renderHookRTL<TResult, TProps>>[1],
) {
  return renderHookRTL(callback, {
    wrapper: ({ children }) => (
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{children}</NextIntlClientProvider>
    ),
    ...options,
  })
}

const { refreshMock } = vi.hoisted(() => ({ refreshMock: vi.fn() }))
vi.mock('next/navigation', () => ({ useRouter: () => ({ refresh: refreshMock }) }))
vi.mock('@/components/ui/use-toast', () => ({ toast: vi.fn() }))
vi.spyOn(console, 'error').mockImplementation(() => {})

const toastMock = vi.mocked(toast)

function stubFetch(ok: boolean, body = '') {
  const fetchMock = vi.fn().mockResolvedValue({ ok, text: async () => body } as Response)
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

beforeEach(() => vi.clearAllMocks())

describe('useMutateAndRefresh', () => {
  it('成功：默认 POST、reload + router.refresh 都调、成功 toast、返回 true', async () => {
    const fetchMock = stubFetch(true)
    const reload = vi.fn()
    const { result } = renderHook(() => useMutateAndRefresh(reload))

    const ok = await result.current('/api/proxy/projects/1/close', { method: 'PUT', okTitle: '已关闭' })

    expect(ok).toBe(true)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe('/api/proxy/projects/1/close')
    expect(init.method).toBe('PUT')
    expect(reload).toHaveBeenCalledTimes(1)
    expect(refreshMock).toHaveBeenCalledTimes(1)
    expect(toastMock).toHaveBeenCalledWith(expect.objectContaining({ variant: 'success', title: '已关闭' }))
  })

  it('带 body：JSON 序列化 + Content-Type', async () => {
    const fetchMock = stubFetch(true)
    const { result } = renderHook(() => useMutateAndRefresh())

    await result.current('/api/proxy/employees/batch-delete', { body: { ids: ['a', 'b'] } })

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(init.headers).toMatchObject({ 'Content-Type': 'application/json' })
    expect(JSON.parse(init.body as string)).toEqual({ ids: ['a', 'b'] })
  })

  it('无 body：不带 Content-Type、不带 body', async () => {
    const fetchMock = stubFetch(true)
    const { result } = renderHook(() => useMutateAndRefresh())

    await result.current('/api/proxy/x/1/toggle', { method: 'PUT' })

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.headers).toBeUndefined()
    expect(init.body).toBeUndefined()
  })

  it('省略 okTitle：不弹成功 toast（静默动作）', async () => {
    stubFetch(true)
    const { result } = renderHook(() => useMutateAndRefresh())

    await result.current('/api/proxy/x/1/silent', { method: 'PUT' })

    expect(toastMock).not.toHaveBeenCalled()
    expect(refreshMock).toHaveBeenCalledTimes(1)
  })

  it('失败：destructive toast、reload/refresh 都不调、返回 false', async () => {
    stubFetch(false, JSON.stringify({ error: { message: '仍有样品挂载' } }))
    const reload = vi.fn()
    const { result } = renderHook(() => useMutateAndRefresh(reload))

    const ok = await result.current('/api/proxy/projects/1', { method: 'DELETE', errorTitle: '删除失败' })

    expect(ok).toBe(false)
    expect(reload).not.toHaveBeenCalled()
    expect(refreshMock).not.toHaveBeenCalled()
    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({ variant: 'destructive', title: '删除失败', description: '仍有样品挂载' }),
    )
  })

  it('errorFallback：后端无可解析消息时用自定义兜底文案', async () => {
    stubFetch(false, '') // 空响应体 → 无 ABP error.message，走兜底
    const { result } = renderHook(() => useMutateAndRefresh())

    await result.current('/api/proxy/exams/1/publish', { errorFallback: '发布要求试卷已上架' })

    expect(toastMock).toHaveBeenCalledWith(
      expect.objectContaining({ variant: 'destructive', description: '发布要求试卷已上架' }),
    )
  })

  it('省略 reload（客户端分页页）：只 router.refresh，不报错', async () => {
    stubFetch(true)
    const { result } = renderHook(() => useMutateAndRefresh())

    const ok = await result.current('/api/proxy/x/1/toggle', { method: 'PUT', okTitle: 'ok' })

    expect(ok).toBe(true)
    expect(refreshMock).toHaveBeenCalledTimes(1)
  })
})
