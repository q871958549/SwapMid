import { afterEach, describe, expect, it, vi } from 'vitest'
import { loadWithChunkRetry } from './load-with-chunk-retry'

afterEach(() => {
  document.head.querySelectorAll('script').forEach(script => script.remove())
  vi.restoreAllMocks()
})

function fixture(src = '/_next/static/chunks/excel-e2e.js') {
  let script!: HTMLScriptElement
  let resolve!: (value: string) => void
  let reject!: (error: Error) => void
  const pending = new Promise<string>((yes, no) => { resolve = yes; reject = no })
  const originalError = vi.fn(() => reject(new Error('cached forever')))
  const originalLoad = vi.fn(() => resolve('loaded'))
  const load = () => {
    if (!script) {
      script = document.createElement('script')
      script.setAttribute('src', src)
      script.nonce = 'test-nonce'
      script.setAttribute('integrity', 'sha256-test')
      script.crossOrigin = 'anonymous'
      script.addEventListener('error', originalError)
      script.addEventListener('load', originalLoad)
      document.head.appendChild(script)
    }
    return pending
  }
  return { load, originalError, originalLoad, resolve, get script() { return script } }
}

function current() { return document.head.querySelector('script')! }

describe('动态分块加载恢复', () => {
  it('首次失败不拒绝运行时缓存，同 URL 重试后原操作继续且保留安全属性', async () => {
    const source = fixture('/_next/static/chunks/excel-e2e.js?dpl=build-a')
    const loading = loadWithChunkRetry(source.load)
    source.script.dispatchEvent(new Event('error'))
    const retry = current()
    expect(retry).not.toBe(source.script)
    expect(source.originalError).not.toHaveBeenCalled()
    expect(retry.getAttribute('src')).toBe('/_next/static/chunks/excel-e2e.js?dpl=build-a')
    expect(retry.nonce).toBe('test-nonce')
    expect(retry.getAttribute('integrity')).toBe('sha256-test')
    expect(retry.crossOrigin).toBe('anonymous')
    retry.dispatchEvent(new Event('load'))
    await expect(loading).resolves.toBe('loaded')
    expect(source.originalLoad).toHaveBeenCalledOnce()
  })

  it('耗尽后明确失败，网络恢复后再次操作可继续同一 Promise，无需刷新', async () => {
    const source = fixture()
    const loading = loadWithChunkRetry(source.load)
    const rejection = expect(loading).rejects.toMatchObject({ name: 'ChunkLoadError' })
    source.script.dispatchEvent(new Event('error'))
    const retry = current()
    retry.dispatchEvent(new Event('error'))
    await rejection
    expect(current()).toBe(retry)
    expect(source.originalError).not.toHaveBeenCalled()
    const recovered = loadWithChunkRetry(source.load)
    expect(current()).not.toBe(retry)
    current().dispatchEvent(new Event('load'))
    await expect(recovered).resolves.toBe('loaded')
  })

  it('同一 loader 并发调用共享重试上限且全部收到最终失败', async () => {
    const source = fixture()
    const one = loadWithChunkRetry(source.load)
    const two = loadWithChunkRetry(source.load)
    const rejections = Promise.all([
      expect(one).rejects.toMatchObject({ name: 'ChunkLoadError' }),
      expect(two).rejects.toMatchObject({ name: 'ChunkLoadError' }),
    ])
    source.script.dispatchEvent(new Event('error'))
    current().dispatchEvent(new Event('error'))
    await rejections
    const recovered = loadWithChunkRetry(source.load)
    current().dispatchEvent(new Event('load'))
    await expect(recovered).resolves.toBe('loaded')
  })

  it('同一 import 多个块先后失败，操作结束后仍保护稍后失败的缓存', async () => {
    const one = fixture('/_next/static/chunks/one.js')
    const two = fixture('/_next/static/chunks/two.js')
    const load = () => Promise.all([one.load(), two.load()])
    const loading = loadWithChunkRetry(load)
    const rejection = expect(loading).rejects.toMatchObject({ name: 'ChunkLoadError' })
    one.script.dispatchEvent(new Event('error'))
    document.head.querySelector('script[src$="one.js"]')!.dispatchEvent(new Event('error'))
    await rejection
    two.script.dispatchEvent(new Event('error'))
    document.head.querySelector('script[src$="two.js"]')!.dispatchEvent(new Event('error'))
    expect(one.originalError).not.toHaveBeenCalled()
    expect(two.originalError).not.toHaveBeenCalled()
    const recovered = loadWithChunkRetry(load)
    document.head.querySelector('script[src$="one.js"]')!.dispatchEvent(new Event('load'))
    document.head.querySelector('script[src$="two.js"]')!.dispatchEvent(new Event('load'))
    await expect(recovered).resolves.toEqual(['loaded', 'loaded'])
  })

  it.each([
    'https://untrusted.example/_next/static/chunks/export.js',
    '/other/export.js',
    '/_next/static/chunks/not-a-script.css',
  ])('不接管其他来源或其他类型资源：%s', async src => {
    const source = fixture(src)
    const loading = loadWithChunkRetry(source.load)
    const rejection = expect(loading).rejects.toThrow('cached forever')
    source.script.dispatchEvent(new Event('error'))
    await rejection
    expect(source.originalError).toHaveBeenCalledOnce()
    expect(current()).toBe(source.script)
  })

  it('不拦截同时发生的其他导航资源错误', async () => {
    const source = fixture()
    const loading = loadWithChunkRetry(source.load)
    const other = document.createElement('script')
    other.src = '/_next/static/chunks/other-navigation.js'
    const onError = vi.fn()
    other.onerror = onError
    document.head.appendChild(other)
    other.dispatchEvent(new Event('error'))
    expect(onError).toHaveBeenCalledOnce()
    expect(other.isConnected).toBe(true)
    source.script.dispatchEvent(new Event('load'))
    await loading
  })

  it('一个加载完成后仍保留另一个加载的恢复监听', async () => {
    const source = fixture()
    const other = fixture('/_next/static/chunks/other.js')
    const one = loadWithChunkRetry(source.load)
    const two = loadWithChunkRetry(other.load)
    source.script.dispatchEvent(new Event('load'))
    await one
    other.script.dispatchEvent(new Event('error'))
    document.head.querySelector('script[src$="other.js"]')!.dispatchEvent(new Event('load'))
    await expect(two).resolves.toBe('loaded')
  })

  it('操作结束即移除监听', async () => {
    const source = fixture()
    const loading = loadWithChunkRetry(source.load)
    source.script.dispatchEvent(new Event('load'))
    await loading
    const onError = vi.fn()
    source.script.addEventListener('error', onError)
    source.script.dispatchEvent(new Event('error'))
    expect(onError).toHaveBeenCalledOnce()
  })
})
