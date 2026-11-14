import { describe, it, expect, vi, afterEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { RemoteCombobox, type RemoteComboboxOption } from './remote-combobox'
import zhHansMessages from '../../../messages/zh-Hans.json'

/**
 * ⚠️ jsdom 局限：同 combobox.test.tsx —— jsdom 不做 CSS hit-testing，
 * 本文件只守新结构的交互协议（清空钮独立于 trigger、onChange 双参语义、
 * 不触发展开），不写「复现旧 bug」的假测试。
 *
 * RemoteCombobox 的默认占位符/空态/搜索中文案（i18n 批次3d）经 useTranslations('Common') 求值，
 * 组件树需要 NextIntlClientProvider 才能渲染；真实 zh-Hans messages 直供，与生产一致。
 */
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

function noopFetch(): Promise<RemoteComboboxOption[]> {
  return Promise.resolve([])
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

afterEach(() => {
  vi.useRealTimers()
})

describe('RemoteCombobox 清空按钮', () => {
  it('已选值 + clearable → 清空按钮存在，点击后 onChange 以 (\'\', undefined) 调用一次且不展开下拉', () => {
    const onChange = vi.fn()
    render(
      <RemoteCombobox
        value="e1"
        selectedLabel="唐远"
        onChange={onChange}
        fetchOptions={noopFetch}
        clearable
      />
    )

    const clearBtn = screen.getByRole('button', { name: '清空已选' })
    fireEvent.click(clearBtn)

    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith('', undefined)
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'false')
  })

  it('未选值时不渲染清空按钮', () => {
    render(<RemoteCombobox onChange={vi.fn()} fetchOptions={noopFetch} clearable />)
    expect(screen.queryByRole('button', { name: '清空已选' })).not.toBeInTheDocument()
  })

  it('clearable=false 时即使已选值也不渲染清空按钮', () => {
    render(<RemoteCombobox value="e1" selectedLabel="唐远" onChange={vi.fn()} fetchOptions={noopFetch} />)
    expect(screen.queryByRole('button', { name: '清空已选' })).not.toBeInTheDocument()
  })

  it('disabled 时即使已选值也不渲染清空按钮', () => {
    render(
      <RemoteCombobox
        value="e1"
        selectedLabel="唐远"
        onChange={vi.fn()}
        fetchOptions={noopFetch}
        clearable
        disabled
      />
    )
    expect(screen.queryByRole('button', { name: '清空已选' })).not.toBeInTheDocument()
  })

  it('点击 trigger 本体正常展开下拉', async () => {
    render(
      <RemoteCombobox value="e1" selectedLabel="唐远" onChange={vi.fn()} fetchOptions={noopFetch} clearable />
    )
    const trigger = screen.getByRole('combobox')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(trigger)

    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    // 空 query 立即拉取（不防抖），等待 fetchOptions 落定，避免 act 警告
    expect(await screen.findByText('无匹配结果')).toBeInTheDocument()
  })
})

describe('RemoteCombobox 范围变化', () => {
  it('scopeKey 变化时重新拉取', async () => {
    const fetchOptions = vi.fn()
      .mockResolvedValueOnce([{ value: 'a', label: '甲' }])
      .mockResolvedValueOnce([{ value: 'b', label: '乙' }])
    const { rerender } = render(
      <RemoteCombobox fetchOptions={fetchOptions} scopeKey="dept-1" debounceMs={0} />,
    )
    fireEvent.click(screen.getByRole('combobox'))
    expect(await screen.findByText('甲')).toBeInTheDocument()

    rerender(
      <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
        <RemoteCombobox fetchOptions={fetchOptions} scopeKey="dept-2" debounceMs={0} />
      </NextIntlClientProvider>,
    )
    expect(await screen.findByText('乙')).toBeInTheDocument()
    expect(fetchOptions).toHaveBeenCalledTimes(2)
  })
})

describe('RemoteCombobox 关闭焦点交接', () => {
  const fetchOptions = () => Promise.resolve([{ value: 'a', label: '选项甲' }])

  it('选中后没有其他控件接管时恢复触发器焦点', async () => {
    render(<RemoteCombobox fetchOptions={fetchOptions} />)
    const trigger = screen.getByRole('combobox')
    fireEvent.click(trigger)
    fireEvent.click(await screen.findByRole('option', { name: '选项甲' }))
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('Escape关闭后仍恢复触发器焦点', async () => {
    render(<RemoteCombobox fetchOptions={fetchOptions} />)
    const trigger = screen.getByRole('combobox')
    fireEvent.click(trigger)
    await screen.findByRole('option', { name: '选项甲' })
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('关闭后的延迟恢复不得夺走其他已连接控件的焦点', async () => {
    render(<><RemoteCombobox fetchOptions={fetchOptions} /><input aria-label="next-field" /></>)
    fireEvent.click(screen.getByRole('combobox'))
    fireEvent.click(await screen.findByRole('option', { name: '选项甲' }))
    const next = screen.getByLabelText('next-field')
    next.focus()
    // FocusScope在卸载后以0ms任务恢复焦点，必须等这次任务执行后检查最终归属。
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })
    expect(next).toHaveFocus()
  })
})

describe('RemoteCombobox 请求生命周期', () => {
  it('已有 A 结果时输入 B，debounce 启动前立即隐藏 A 并显示 loading', async () => {
    vi.useFakeTimers()
    const fetchOptions = vi.fn().mockResolvedValue([{ value: 'a', label: '结果 A' }])

    render(<RemoteCombobox fetchOptions={fetchOptions} debounceMs={1_000} />)
    fireEvent.click(screen.getByRole('combobox'))
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(screen.getByText('结果 A')).toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText('输入关键字搜索...'), { target: { value: 'B' } })

    // 不推进 1 秒 debounce：切词当下就不能让旧结果继续可见、可选。
    expect(fetchOptions).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('结果 A')).not.toBeInTheDocument()
    expect(screen.getByText('搜索中...')).toBeInTheDocument()
  })

  it('搜索词由 A 切到 B 时立即 abort A，且 A 晚到不能覆盖 B', async () => {
    const requestA = deferred<RemoteComboboxOption[]>()
    const requestB = deferred<RemoteComboboxOption[]>()
    const signals = new Map<string, AbortSignal>()
    const fetchOptions = vi.fn((query: string, signal: AbortSignal) => {
      signals.set(query, signal)
      if (query === 'A') return requestA.promise
      if (query === 'B') return requestB.promise
      return Promise.resolve([])
    })

    render(<RemoteCombobox fetchOptions={fetchOptions} debounceMs={0} />)
    fireEvent.click(screen.getByRole('combobox'))
    await screen.findByText('无匹配结果')

    const search = screen.getByPlaceholderText('输入关键字搜索...')
    fireEvent.change(search, { target: { value: 'A' } })
    await waitFor(() => expect(fetchOptions).toHaveBeenCalledWith('A', expect.any(AbortSignal)))
    fireEvent.change(search, { target: { value: 'B' } })
    await waitFor(() => expect(fetchOptions).toHaveBeenCalledWith('B', expect.any(AbortSignal)))

    expect(signals.get('A')?.aborted).toBe(true)
    expect(signals.get('B')?.aborted).toBe(false)

    await act(async () => {
      requestB.resolve([{ value: 'b', label: '结果 B' }])
      await requestB.promise
    })
    expect(await screen.findByText('结果 B')).toBeInTheDocument()

    // 模拟底层 fetch/polyfill 忽略 abort，旧请求仍然成功返回；代际门禁必须继续挡住它。
    await act(async () => {
      requestA.resolve([{ value: 'a', label: '过期结果 A' }])
      await requestA.promise
    })
    expect(screen.queryByText('过期结果 A')).not.toBeInTheDocument()
    expect(screen.getByText('结果 B')).toBeInTheDocument()
  })

  it('fetchOptions 不接收 signal 时，仍由请求代际阻止旧响应写回', async () => {
    const requestA = deferred<RemoteComboboxOption[]>()
    const requestB = deferred<RemoteComboboxOption[]>()
    const fetchOptions = vi.fn((query: string) => {
      if (query === 'A') return requestA.promise
      if (query === 'B') return requestB.promise
      return Promise.resolve([])
    })

    render(<RemoteCombobox fetchOptions={fetchOptions} debounceMs={0} />)
    fireEvent.click(screen.getByRole('combobox'))
    await screen.findByText('无匹配结果')

    const search = screen.getByPlaceholderText('输入关键字搜索...')
    fireEvent.change(search, { target: { value: 'A' } })
    await waitFor(() => expect(fetchOptions).toHaveBeenCalledWith('A', expect.any(AbortSignal)))
    fireEvent.change(search, { target: { value: 'B' } })
    await waitFor(() => expect(fetchOptions).toHaveBeenCalledWith('B', expect.any(AbortSignal)))

    await act(async () => {
      requestB.resolve([{ value: 'b', label: '最新结果' }])
      await requestB.promise
    })
    expect(await screen.findByText('最新结果')).toBeInTheDocument()

    await act(async () => {
      requestA.resolve([{ value: 'a', label: '被阻断的旧结果' }])
      await requestA.promise
    })
    expect(screen.queryByText('被阻断的旧结果')).not.toBeInTheDocument()
    expect(screen.getByText('最新结果')).toBeInTheDocument()
  })

  it('关闭下拉时 abort 在途请求，晚到结果不能写入下一次打开的列表', async () => {
    vi.useFakeTimers()
    const pending = deferred<RemoteComboboxOption[]>()
    let signal: AbortSignal | undefined
    const fetchOptions = vi.fn((_query: string, requestSignal: AbortSignal) => {
      signal = requestSignal
      return pending.promise
    })

    render(<RemoteCombobox fetchOptions={fetchOptions} debounceMs={0} />)
    const trigger = screen.getByRole('combobox')
    fireEvent.click(trigger)
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(fetchOptions).toHaveBeenCalledTimes(1)

    fireEvent.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(signal?.aborted).toBe(true)

    await act(async () => {
      pending.resolve([{ value: 'stale', label: '关闭后晚到结果' }])
      await pending.promise
    })

    // 不推进重新打开后排队的 0ms 拉取，以便直接观察关闭期间是否错误写入了 options。
    fireEvent.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(screen.queryByText('关闭后晚到结果')).not.toBeInTheDocument()
  })

  it('组件卸载时 abort 在途请求', async () => {
    const pending = deferred<RemoteComboboxOption[]>()
    let signal: AbortSignal | undefined
    const fetchOptions = vi.fn((_query: string, requestSignal: AbortSignal) => {
      signal = requestSignal
      return pending.promise
    })
    const { unmount } = render(<RemoteCombobox fetchOptions={fetchOptions} debounceMs={0} />)

    fireEvent.click(screen.getByRole('combobox'))
    await waitFor(() => expect(fetchOptions).toHaveBeenCalledTimes(1))
    expect(signal?.aborted).toBe(false)

    unmount()
    expect(signal?.aborted).toBe(true)

    await act(async () => {
      pending.resolve([])
      await pending.promise
    })
  })
})
