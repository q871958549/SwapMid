import { beforeEach, describe, expect, it } from 'vitest'
import {
  authenticatedBrowserStoragePolicies,
  createAuthenticatedBrowserStorage,
} from '@/lib/browser-storage-policy'
import {
  clearApiConsoleHistory,
  isApiConsoleHistory,
  readApiConsoleHistory,
  writeApiConsoleHistory,
} from './client'

const policy = authenticatedBrowserStoragePolicies.apiConsoleHistory

function createStorage() {
  return createAuthenticatedBrowserStorage({
    identityVersion: 'identity-console-test',
    storagePrincipalId: 'sp1-console-test',
  })
}

beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
})

describe('API 测试控制台历史校验', () => {
  it('只接受 method/path/at 严格元数据结构', () => {
    const now = Date.now()
    expect(isApiConsoleHistory([
      { method: 'GET', path: '/api/v1/projects?status=active', at: now },
      { method: 'POST', path: 'enterprises', at: now - 1 },
    ])).toBe(true)
    expect(isApiConsoleHistory([])).toBe(true)

    expect(isApiConsoleHistory([
      { method: 'GET', path: '/api/v1/projects', at: now, body: '{"secret":true}' },
    ])).toBe(false)
    expect(isApiConsoleHistory([
      { method: 'GET', path: '/api/v1/projects', at: now, headersText: 'Authorization: secret' },
    ])).toBe(false)
    expect(isApiConsoleHistory([
      { method: 'OPTIONS', path: '/api/v1/projects', at: now },
    ])).toBe(false)
    expect(isApiConsoleHistory([
      { method: 'GET', path: ' /api/v1/projects ', at: now },
    ])).toBe(false)
    expect(isApiConsoleHistory([
      { method: 'GET', path: '/api/v1/projects\nforged', at: now },
    ])).toBe(false)
    expect(isApiConsoleHistory([
      { method: 'GET', path: `/${'x'.repeat(2_048)}`, at: now },
    ])).toBe(false)
    expect(isApiConsoleHistory([
      { method: 'GET', path: '/api/v1/projects', at: now + 60_000 },
    ])).toBe(false)
    expect(isApiConsoleHistory(Array.from(
      { length: 21 },
      (_, index) => ({ method: 'GET', path: `/api/v1/${index}`, at: now }),
    ))).toBe(false)
  })

  it('坏数据读取后由策略层立即清理', () => {
    const storage = createStorage()
    const key = storage.key(policy)
    sessionStorage.setItem(key, JSON.stringify({
      version: policy.version,
      savedAt: Date.now(),
      value: [{
        method: 'POST',
        path: '/api/v1/projects',
        at: Date.now(),
        body: '{"password":"secret"}',
      }],
    }))

    expect(readApiConsoleHistory(storage)).toEqual([])
    expect(sessionStorage.getItem(key)).toBeNull()
  })
})

describe('API 测试控制台历史持久化', () => {
  it('写入时剥离 headers/body，仅保留低敏元数据', () => {
    const storage = createStorage()
    const at = Date.now()

    expect(writeApiConsoleHistory(storage, [{
      method: 'POST',
      path: '/api/v1/projects',
      at,
      body: '{"password":"secret"}',
      headersText: 'Authorization: Bearer secret',
    }])).toBe(true)

    expect(readApiConsoleHistory(storage)).toEqual([{
      method: 'POST',
      path: '/api/v1/projects',
      at,
    }])
    const raw = sessionStorage.getItem(storage.key(policy))
    expect(raw).not.toContain('password')
    expect(raw).not.toContain('Authorization')
    expect(raw).not.toContain('secret')
  })

  it('清空历史删除当前身份作用域的数据', () => {
    const storage = createStorage()
    storage.write(policy, [{
      method: 'GET',
      path: '/api/v1/projects',
      at: Date.now(),
    }])

    clearApiConsoleHistory(storage)

    expect(sessionStorage.getItem(storage.key(policy))).toBeNull()
    expect(readApiConsoleHistory(storage)).toEqual([])
  })
})
