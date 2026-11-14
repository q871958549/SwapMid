import { describe, expect, it } from 'vitest'
import {
  bindSessionIdentityToUrl,
  buildSessionUnavailablePath,
  extractSessionIdentitySearch,
} from './session-availability'

describe('session identity query', () => {
  it('提取内部版本参数并逐字保留其余查询串', () => {
    expect(extractSessionIdentitySearch(
      '?filter=a%20b&_swapmidIdentityVersion=identity-a&tag=x&tag=y',
    )).toEqual({
      identityVersions: ['identity-a'],
      forwardedSearch: '?filter=a%20b&tag=x&tag=y',
    })
  })

  it('识别编码后的同名键、重复参数与非法百分号编码', () => {
    expect(extractSessionIdentitySearch(
      '?%5FswapmidIdentityVersion=a&_swapmidIdentityVersion=b',
    )?.identityVersions).toEqual(['a', 'b'])
    expect(extractSessionIdentitySearch('?_swapmidIdentityVersion=%ZZ')).toBeNull()
  })

  it('绑定原生 GET 时替换旧版本、保留原编码与 hash', () => {
    expect(bindSessionIdentityToUrl(
      '/api/proxy/export?name=a%20b&_swapmidIdentityVersion=old#preview',
      'identity-new',
    )).toBe(
      '/api/proxy/export?name=a%20b&_swapmidIdentityVersion=identity-new#preview',
    )
  })

  it('拒绝跨域/协议相对 URL 与非法查询串', () => {
    expect(() => bindSessionIdentityToUrl(
      'https://evil.example/file',
      'identity-a',
    )).toThrow('same-origin')
    expect(() => bindSessionIdentityToUrl(
      '//evil.example/file',
      'identity-a',
    )).toThrow('same-origin')
    expect(() => bindSessionIdentityToUrl(
      '/api/proxy/file?bad=%ZZ',
      'identity-a',
    )).toThrow('invalid query')
  })

  it('构建会话存储不可用跳转路径并编码回跳地址', () => {
    expect(buildSessionUnavailablePath('/scan/ota?page=2&q=a b')).toBe(
      '/api/auth/session-unavailable?redirect=%2Fscan%2Fota%3Fpage%3D2%26q%3Da%20b',
    )
  })
})
