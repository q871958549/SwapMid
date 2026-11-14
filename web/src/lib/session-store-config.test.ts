import { describe, expect, it } from 'vitest'
import { resolveSessionStoreConfiguration } from './session-store-config'

describe('session store 配置门禁', () => {
  it('生产必须显式选择 redis 且提供 URL', () => {
    expect(() => resolveSessionStoreConfiguration({
      NODE_ENV: 'production',
      REDIS_URL: 'redis://redis:6379/1',
    })).toThrow('SESSION_STORE_MODE=redis is required')

    expect(() => resolveSessionStoreConfiguration({
      NODE_ENV: 'production',
      SESSION_STORE_MODE: 'redis',
    })).toThrow('REDIS_URL is required')

    expect(() => resolveSessionStoreConfiguration({
      NODE_ENV: 'production',
      SESSION_STORE_MODE: 'memory',
    })).toThrow('memory mode is forbidden')

    expect(resolveSessionStoreConfiguration({
      NODE_ENV: 'production',
      SESSION_STORE_MODE: 'redis',
      REDIS_URL: 'redis://:secret@redis:6379/1',
    })).toEqual({
      mode: 'redis',
      redisUrl: 'redis://:secret@redis:6379/1',
    })
  })

  it('开发与测试可显式使用内存，未显式配置时保持便利推断', () => {
    expect(resolveSessionStoreConfiguration({
      NODE_ENV: 'development',
    })).toEqual({ mode: 'memory' })
    expect(resolveSessionStoreConfiguration({
      NODE_ENV: 'test',
      REDIS_URL: 'redis://redis.test:6379/1',
    })).toEqual({
      mode: 'redis',
      redisUrl: 'redis://redis.test:6379/1',
    })
    expect(resolveSessionStoreConfiguration({
      NODE_ENV: 'test',
      SESSION_STORE_MODE: 'memory',
      REDIS_URL: 'redis://ignored.test:6379/1',
    })).toEqual({ mode: 'memory' })
  })

  it.each([
    ['NODE_ENV 缺失', {}],
    ['staging', { NODE_ENV: 'staging' }],
    ['prod', { NODE_ENV: 'prod' }],
    ['NODE_ENV 空字符串', { NODE_ENV: '' }],
  ])('%s 不得静默退回内存', (_label, environment) => {
    expect(() => resolveSessionStoreConfiguration(environment)).toThrow(
      'SESSION_STORE_MODE=redis is required outside development/test',
    )
    expect(() => resolveSessionStoreConfiguration({
      ...environment,
      SESSION_STORE_MODE: 'memory',
    })).toThrow('memory mode is forbidden outside development/test')
  })

  it.each([
    ['未知 mode', { NODE_ENV: 'test', SESSION_STORE_MODE: 'auto' }],
    ['mode 首尾空白', { NODE_ENV: 'test', SESSION_STORE_MODE: ' redis' }],
    ['非 Redis 协议', {
      NODE_ENV: 'test',
      SESSION_STORE_MODE: 'redis',
      REDIS_URL: 'https://redis.example/1',
    }],
    ['URL 首尾空白', {
      NODE_ENV: 'test',
      SESSION_STORE_MODE: 'redis',
      REDIS_URL: ' redis://redis.test:6379/1',
    }],
    ['URL fragment', {
      NODE_ENV: 'test',
      SESSION_STORE_MODE: 'redis',
      REDIS_URL: 'redis://redis.test:6379/1#secret',
    }],
  ])('%s 必须失败关闭', (_label, environment) => {
    expect(() => resolveSessionStoreConfiguration(environment)).toThrow(
      'Session store configuration is invalid',
    )
  })
})
