import { describe, expect, it } from 'vitest'
import { applyTrustedClientIp, EDGE_TOKEN_HEADER, readTrustedClientIp } from './trusted-client-ip'

describe('readTrustedClientIp', () => {
  it('接受单个 X-Forwarded-For，并去掉两端空白', () => {
    expect(readTrustedClientIp(new Headers({ 'x-forwarded-for': '203.0.113.20' }))).toBe('203.0.113.20')
    expect(readTrustedClientIp(new Headers({ 'x-forwarded-for': ' 2001:db8::1 ' }))).toBe('2001:db8::1')
    expect(readTrustedClientIp(new Headers())).toBeUndefined()
  })

  it('两个来源头一致时才同时采信，大小写不同的 IPv6 视为同一地址', () => {
    const headers = new Headers({
      'x-forwarded-for': '2001:db8::1',
      'x-swapmid-client-ip': '2001:DB8::1',
    })
    expect(readTrustedClientIp(headers)).toBe('2001:db8::1')
  })

  it('只有专用来源头、或两个头不一致时不转发', () => {
    expect(readTrustedClientIp(new Headers({ 'x-swapmid-client-ip': '203.0.113.20' }))).toBeUndefined()
    expect(readTrustedClientIp(new Headers({
      'x-forwarded-for': '203.0.113.20',
      'x-swapmid-client-ip': '198.51.100.8',
    }))).toBeUndefined()
  })

  it.each([
    '203.0.113.20, 10.0.0.1',
    '203.0.113.20:443',
    'unknown',
    'not-an-ip',
    `203.0.113.20${' '.repeat(128)}`,
    '::ffff:203.0.113.20',
  ])('拒绝链、端口、unknown、非法、超长或 IPv4 映射地址：%s', value => {
    // 不用 Headers：实现会裁掉首尾空白，超长用例必须按原始字符串判断。
    expect(readTrustedClientIp({ get: () => value })).toBeUndefined()
  })

  it('专用来源头格式异常时，即使 X-Forwarded-For 是单个合法 IP 也不转发', () => {
    expect(readTrustedClientIp(new Headers({
      'x-forwarded-for': '203.0.113.20',
      'x-swapmid-client-ip': 'unknown',
    }))).toBeUndefined()
  })

  it('不读取 X-Real-IP', () => {
    expect(readTrustedClientIp(new Headers({ 'x-real-ip': '203.0.113.20' }))).toBeUndefined()
  })
})

describe('applyTrustedClientIp', () => {
  it('只写入单个可信 IP，并清掉调用方已经放上的来源链', () => {
    const target = new Headers({
      authorization: 'Bearer server-token',
      'x-forwarded-for': '198.51.100.9, 203.0.113.20',
      'x-real-ip': '198.51.100.9',
    })
    applyTrustedClientIp(target, new Headers({ 'x-forwarded-for': '203.0.113.20' }))

    expect(target.get('authorization')).toBe('Bearer server-token')
    expect(target.get('x-forwarded-for')).toBe('203.0.113.20')
    expect(target.get('x-swapmid-client-ip')).toBe('203.0.113.20')
    expect(target.get('x-real-ip')).toBeNull()
  })

  it('取不到可信 IP 时不写来源头', () => {
    const target = new Headers({ 'x-forwarded-for': '203.0.113.20, 10.0.0.1' })
    applyTrustedClientIp(target, new Headers({ 'x-forwarded-for': '203.0.113.20, 10.0.0.1' }))

    expect(target.get('x-forwarded-for')).toBeNull()
    expect(target.get('x-swapmid-client-ip')).toBeNull()
  })

  it('转发格式合法的边车令牌，并丢掉调用方放上的短值', () => {
    const token = 'abcdefghijklmnopqrstuvwxyz012345'
    const target = new Headers({ [EDGE_TOKEN_HEADER]: 'short-token-from-caller-is-ignored!!' })
    applyTrustedClientIp(target, new Headers({
      'x-forwarded-for': '198.51.100.10',
      'x-swapmid-client-ip': '198.51.100.10',
      'x-swapmid-edge-token': token,
    }))

    expect(target.get('x-swapmid-edge-token')).toBe(token)
    applyTrustedClientIp(target, new Headers({ 'x-swapmid-edge-token': 'not a token' }))
    expect(target.get('x-swapmid-edge-token')).toBeNull()
  })
})
