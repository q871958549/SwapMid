import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchAbpUserConfig } from './abp-config'

let fetchMock: ReturnType<typeof vi.fn>

beforeEach(() => {
  fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
})

function configResponse(currentUser: Record<string, unknown>): Response {
  return {
    ok: true,
    json: async () => ({
      currentUser,
      auth: { grantedPolicies: {} },
      setting: { values: {} },
    }),
  } as Response
}

describe('fetchAbpUserConfig · 稳定账号主键', () => {
  it('读取并规范化 ABP currentUser.id', async () => {
    fetchMock.mockResolvedValue(configResponse({
      id: '  user-id-1  ',
      userName: 'admin',
      name: '管理员',
    }))

    await expect(fetchAbpUserConfig('access-token')).resolves.toEqual(
      expect.objectContaining({
        userId: 'user-id-1',
        userName: 'admin',
      }),
    )
  })

  it.each([
    ['缺失', undefined],
    ['空白', '   '],
    ['非字符串', 42],
    ['超长', 'x'.repeat(257)],
  ])('%s id 按旧响应兼容为 undefined', async (_label, id) => {
    fetchMock.mockResolvedValue(configResponse({
      id,
      userName: 'legacy-user',
      name: '旧会话用户',
    }))

    await expect(fetchAbpUserConfig('access-token')).resolves.toEqual(
      expect.objectContaining({
        userId: undefined,
        userName: 'legacy-user',
      }),
    )
  })
})
