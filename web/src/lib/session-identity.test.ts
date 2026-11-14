import { describe, expect, it } from 'vitest'
import { getSessionStoragePrincipalId } from './session-identity'

describe('getSessionStoragePrincipalId', () => {
  it('优先使用稳定 userId，同一账号改名不改变存储隔离键', () => {
    const beforeRename = getSessionStoragePrincipalId({
      userId: 'user-id-1',
      userName: 'old-name',
    })
    const afterRename = getSessionStoragePrincipalId({
      userId: 'user-id-1',
      userName: 'new-name',
    })

    expect(afterRename).toBe(beforeRename)
    expect(beforeRename).toMatch(/^sp1-[A-Za-z0-9_-]{43}$/)
    expect(beforeRename).toHaveLength(47)
    expect(beforeRename).not.toContain('user-id-1')
  })

  it('不同 userId 产生不同隔离键', () => {
    expect(getSessionStoragePrincipalId({
      userId: 'user-id-1',
      userName: 'same-name',
    })).not.toBe(getSessionStoragePrincipalId({
      userId: 'user-id-2',
      userName: 'same-name',
    }))
  })

  it('旧会话回退 NFKC、去首尾空白且忽略用户名大小写', () => {
    expect(getSessionStoragePrincipalId({
      userName: '  Ａdmin ',
    })).toBe(getSessionStoragePrincipalId({
      userName: 'ADMIN',
    }))
  })

  it('userId 与同文本 userName 使用不同哈希域', () => {
    expect(getSessionStoragePrincipalId({
      userId: 'admin',
      userName: 'ignored',
    })).not.toBe(getSessionStoragePrincipalId({
      userName: 'admin',
    }))
  })
})
