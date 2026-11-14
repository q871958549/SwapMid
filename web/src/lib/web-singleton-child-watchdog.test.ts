import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  WEB_LEASE_HEARTBEAT_PROTOCOL,
  WEB_LEASE_HEARTBEAT_TYPE,
} from './web-singleton-lease-core.mjs'
import {
  startWebSingletonParentWatchdog,
} from './web-singleton-child-watchdog.mjs'

class FakeParentChannel extends EventEmitter {
  connected = true
}

const productionEnvironment = {
  NODE_ENV: 'production',
  SWAPMID_WEB_LEASE_SUPERVISED: '1',
  SWAPMID_WEB_LEASE_WATCHDOG_MS: '1000',
}

afterEach(() => {
  vi.useRealTimers()
})

describe('Next child parent-lease watchdog', () => {
  it('合法心跳重置单调倒计时，过期立即非零终止', async () => {
    vi.useFakeTimers()
    const channel = new FakeParentChannel()
    const terminate = vi.fn()
    startWebSingletonParentWatchdog({
      environment: productionEnvironment,
      processLike: channel,
      terminate,
      logError: vi.fn(),
    })

    await vi.advanceTimersByTimeAsync(900)
    expect(terminate).not.toHaveBeenCalled()
    channel.emit('message', {
      type: WEB_LEASE_HEARTBEAT_TYPE,
      protocol: WEB_LEASE_HEARTBEAT_PROTOCOL,
    })
    await vi.advanceTimersByTimeAsync(999)
    expect(terminate).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(terminate).toHaveBeenCalledWith(79)
  })

  it('父进程 SIGKILL 导致 IPC disconnect 时子进程立即自杀', () => {
    const channel = new FakeParentChannel()
    const terminate = vi.fn()
    startWebSingletonParentWatchdog({
      environment: productionEnvironment,
      processLike: channel,
      terminate,
      logError: vi.fn(),
    })

    channel.connected = false
    channel.emit('disconnect')
    expect(terminate).toHaveBeenCalledWith(79)
  })

  it('伪造环境标记但没有唯一 IPC 通道仍失败关闭', () => {
    const channel = new FakeParentChannel()
    channel.connected = false

    expect(() => startWebSingletonParentWatchdog({
      environment: productionEnvironment,
      processLike: channel,
      terminate: vi.fn(),
      logError: vi.fn(),
    })).toThrow('no parent IPC channel')
  })

  it('无效消息不能延长 watchdog', async () => {
    vi.useFakeTimers()
    const channel = new FakeParentChannel()
    const terminate = vi.fn()
    startWebSingletonParentWatchdog({
      environment: productionEnvironment,
      processLike: channel,
      terminate,
      logError: vi.fn(),
    })

    await vi.advanceTimersByTimeAsync(900)
    channel.emit('message', { type: 'not-a-lease-heartbeat' })
    await vi.advanceTimersByTimeAsync(100)
    expect(terminate).toHaveBeenCalledWith(79)
  })

  it('development 不启用生产 watchdog', async () => {
    vi.useFakeTimers()
    const terminate = vi.fn()
    startWebSingletonParentWatchdog({
      environment: { NODE_ENV: 'development' },
      processLike: new FakeParentChannel(),
      terminate,
      logError: vi.fn(),
    })
    await vi.runAllTimersAsync()
    expect(terminate).not.toHaveBeenCalled()
  })
})
