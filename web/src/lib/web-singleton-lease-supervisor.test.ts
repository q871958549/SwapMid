import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  WebSingletonLeaseAlreadyHeldError,
  WebSingletonLeaseBackendError,
} from './web-singleton-lease-core.mjs'
import {
  WebSingletonLeaseSupervisor,
} from './web-singleton-lease-supervisor.mjs'

class FakeChild extends EventEmitter {
  connected = true
  signals: string[] = []
  messages: unknown[] = []
  closeOnSignal = true

  send(message: unknown, callback?: (error: Error | null) => void): boolean {
    if (!this.connected) {
      callback?.(new Error('IPC disconnected'))
      return false
    }
    this.messages.push(message)
    callback?.(null)
    return true
  }

  kill(signal: string): boolean {
    this.signals.push(signal)
    if (this.closeOnSignal) {
      queueMicrotask(() => {
        this.connected = false
        this.emit('close', 0, null)
      })
    }
    return true
  }
}

function createLease(overrides: Partial<{
  acquire: () => Promise<boolean>
  renew: () => Promise<boolean>
  release: () => Promise<boolean>
}> = {}) {
  return {
    acquire: vi.fn(async () => true),
    renew: vi.fn(async () => true),
    release: vi.fn(async () => true),
    ...overrides,
  }
}

function createSupervisor({
  lease = createLease(),
  child = new FakeChild(),
  spawnChild = () => child,
  logError = vi.fn(),
  renewIntervalMs = 100,
  takeoverIsolationMs = 0,
  heartbeatIntervalMs = 50,
  heartbeatFreshnessMs = 1_000,
} = {}) {
  return {
    lease,
    child,
    supervisor: new WebSingletonLeaseSupervisor({
      lease,
      spawnChild,
      renewIntervalMs,
      takeoverIsolationMs,
      heartbeatIntervalMs,
      heartbeatFreshnessMs,
      shutdownGraceMs: 300,
      failureGraceMs: 20,
      forceKillWaitMs: 10,
      logError,
    }),
  }
}

afterEach(() => {
  vi.useRealTimers()
})

describe('production web singleton supervisor', () => {
  it('拒绝第二实例且绝不启动 Next 子进程', async () => {
    const spawnChild = vi.fn(() => new FakeChild())
    const { supervisor } = createSupervisor({
      lease: createLease({ acquire: vi.fn(async () => false) }),
      spawnChild,
    })

    await expect(supervisor.start()).rejects.toBeInstanceOf(
      WebSingletonLeaseAlreadyHeldError,
    )
    expect(spawnChild).not.toHaveBeenCalled()
  })

  it('Redis 获取租约异常时失败关闭且绝不启动子进程', async () => {
    const spawnChild = vi.fn(() => new FakeChild())
    const { supervisor } = createSupervisor({
      lease: createLease({
        acquire: vi.fn(async () => {
          throw new Error('connection lost')
        }),
      }),
      spawnChild,
    })

    await expect(supervisor.start()).rejects.toBeInstanceOf(
      WebSingletonLeaseBackendError,
    )
    expect(spawnChild).not.toHaveBeenCalled()
  })

  it('新 owner 在接管隔离期持续续租且期满前绝不 spawn', async () => {
    vi.useFakeTimers()
    const spawnChild = vi.fn(() => new FakeChild())
    const { supervisor, lease } = createSupervisor({
      spawnChild,
      takeoverIsolationMs: 300,
      renewIntervalMs: 100,
    })

    const start = supervisor.start()
    await vi.advanceTimersByTimeAsync(299)
    expect(spawnChild).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    await start

    expect(lease.renew).toHaveBeenCalledTimes(3)
    expect(spawnChild).toHaveBeenCalledOnce()
    await supervisor.requestShutdown()
  })

  it('隔离期间 key 被删或 owner 换代时失败关闭且不启动旧旁路', async () => {
    vi.useFakeTimers()
    const spawnChild = vi.fn(() => new FakeChild())
    const { supervisor, lease } = createSupervisor({
      lease: createLease({ renew: vi.fn(async () => false) }),
      spawnChild,
      takeoverIsolationMs: 300,
      renewIntervalMs: 100,
    })

    const start = expect(supervisor.start()).rejects.toBeInstanceOf(
      WebSingletonLeaseBackendError,
    )
    await vi.advanceTimersByTimeAsync(100)

    await start
    expect(spawnChild).not.toHaveBeenCalled()
    expect(lease.release).toHaveBeenCalledOnce()
  })

  it('续租丢失后快速停止子进程并非零退出', async () => {
    vi.useFakeTimers()
    const { supervisor, child, lease } = createSupervisor({
      lease: createLease({ renew: vi.fn(async () => false) }),
    })

    await supervisor.start()
    await vi.advanceTimersByTimeAsync(100)
    const result = await supervisor.completion

    expect(child.signals).toContain('SIGTERM')
    expect(result.exitCode).toBe(1)
    expect(lease.release).toHaveBeenCalledOnce()
  })

  it('续租 Redis 异常与 owner 丢失使用同一失败关闭路径', async () => {
    vi.useFakeTimers()
    const { supervisor, child } = createSupervisor({
      lease: createLease({
        renew: vi.fn(async () => {
          throw new Error('timeout')
        }),
      }),
    })

    await supervisor.start()
    await vi.advanceTimersByTimeAsync(100)
    const result = await supervisor.completion

    expect(child.signals).toContain('SIGTERM')
    expect(result.exitCode).toBe(1)
  })

  it('只在最近租约确认仍有余量时发送 IPC 心跳', async () => {
    vi.useFakeTimers()
    const { supervisor, child } = createSupervisor({
      renewIntervalMs: 1_000,
      heartbeatIntervalMs: 50,
      heartbeatFreshnessMs: 120,
    })

    await supervisor.start()
    expect(child.messages).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(100)
    expect(child.messages).toHaveLength(3)
    await vi.advanceTimersByTimeAsync(50)
    const result = await supervisor.completion

    expect(result.exitCode).toBe(1)
    expect(child.signals).toContain('SIGTERM')
  })

  it('ChildProcess 异步 error 被状态机接管且日志不拼接错误内容', async () => {
    const child = new FakeChild()
    const logError = vi.fn()
    const { supervisor } = createSupervisor({ child, logError })

    await supervisor.start()
    child.emit('error', new Error('redis://user:secret@example.invalid/1'))
    const result = await supervisor.completion

    expect(result.exitCode).toBe(1)
    expect(logError).toHaveBeenCalledWith(
      '[web-singleton] web server child process failed; stopping',
    )
    expect(JSON.stringify(logError.mock.calls)).not.toContain('secret')
  })

  it('父子 IPC 意外断开时父进程也进入失败关闭', async () => {
    const child = new FakeChild()
    const { supervisor } = createSupervisor({ child })

    await supervisor.start()
    child.connected = false
    child.emit('disconnect')
    const result = await supervisor.completion

    expect(child.signals).toContain('SIGTERM')
    expect(result.exitCode).toBe(1)
  })

  it('Next 子进程意外 code=0 退出仍视为故障', async () => {
    const child = new FakeChild()
    const { supervisor, lease } = createSupervisor({ child })

    await supervisor.start()
    child.connected = false
    child.emit('close', 0, null)
    const result = await supervisor.completion

    expect(result.exitCode).toBe(1)
    expect(lease.release).toHaveBeenCalledOnce()
  })

  it('正常 SIGTERM 等子进程 close 后才 compare-owner 释放', async () => {
    const events: string[] = []
    const child = new FakeChild()
    child.closeOnSignal = false
    const lease = createLease({
      release: vi.fn(async () => {
        events.push('release')
        return true
      }),
    })
    const { supervisor } = createSupervisor({ lease, child })

    await supervisor.start()
    const completion = supervisor.requestShutdown({ signal: 'SIGTERM' })
    expect(lease.release).not.toHaveBeenCalled()
    events.push('child-close')
    child.connected = false
    child.emit('close', 0, null)
    const result = await completion

    expect(events).toEqual(['child-close', 'release'])
    expect(result.exitCode).toBe(0)
  })

  it('优雅窗口超时后强杀，确认 close 前仍不释放租约', async () => {
    vi.useFakeTimers()
    const child = new FakeChild()
    child.closeOnSignal = false
    const lease = createLease()
    const { supervisor } = createSupervisor({ lease, child })

    await supervisor.start()
    const completion = supervisor.requestShutdown({ signal: 'SIGTERM' })
    await vi.advanceTimersByTimeAsync(300)

    expect(child.signals).toEqual(['SIGTERM', 'SIGKILL'])
    expect(lease.release).not.toHaveBeenCalled()

    child.connected = false
    child.emit('close', null, 'SIGKILL')
    const result = await completion
    expect(lease.release).toHaveBeenCalledOnce()
    expect(result.exitCode).toBe(1)
  })

  it('有界强杀后仍未确认 close 时父进程非零硬退出且不删租约', async () => {
    vi.useFakeTimers()
    const child = new FakeChild()
    child.closeOnSignal = false
    const lease = createLease()
    const { supervisor } = createSupervisor({ lease, child })

    await supervisor.start()
    const completion = supervisor.requestShutdown({ signal: 'SIGTERM' })
    await vi.advanceTimersByTimeAsync(310)
    const result = await completion

    expect(child.signals).toEqual(['SIGTERM', 'SIGKILL'])
    expect(result).toEqual({
      exitCode: 1,
      hardExit: true,
      leaseReleased: false,
    })
    expect(lease.release).not.toHaveBeenCalled()
  })
})
