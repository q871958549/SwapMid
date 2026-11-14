import { afterEach, describe, expect, it, vi } from 'vitest'
import { streamForwardedResponseBody } from '../backend-forwarding'
import {
  buildProxyBackendPath,
  getProxyRequestPolicy,
  isValidProxyQuery,
  PROXY_DEFAULT_MAX_BODY_BYTES,
  resolveProxyApiBase,
  selectProxyRequestHeaders,
  selectProxyResponseHeaders,
} from './proxy-forwarding'

const KIB = 1024
const MIB = 1024 * KIB
const GUID = '123e4567-e89b-12d3-a456-426614174000'
const MULTIPART = 'multipart/form-data; boundary=----SwapMidBoundary123'

afterEach(() => {
  vi.useRealTimers()
})

describe('proxy 回源路径与基址', () => {
  it('逐段编码自定义路径，并对 api / gateway / swagger 前缀保留根路径', () => {
    expect(buildProxyBackendPath(['employees', '研发 部?门'])).toEqual({
      backendPath: '/api/v1/employees/%E7%A0%94%E5%8F%91%20%E9%83%A8%3F%E9%97%A8',
      canonicalPath: '/api/v1/employees/研发 部?门',
    })
    expect(buildProxyBackendPath(['api', 'identity', 'users'])).toEqual({
      backendPath: '/api/identity/users',
      canonicalPath: '/api/identity/users',
    })
    expect(buildProxyBackendPath(['gateway', 'orders', 'list'])).toEqual({
      backendPath: '/gateway/orders/list',
      canonicalPath: '/gateway/orders/list',
    })
    expect(buildProxyBackendPath(['swagger', 'v1', 'swagger.json'])).toEqual({
      backendPath: '/swagger/v1/swagger.json',
      canonicalPath: '/swagger/v1/swagger.json',
    })
  })

  it.each([
    { segments: [] },
    { segments: ['..'] },
    { segments: ['employees', '.'] },
    { segments: ['employees', 'a/b'] },
    { segments: ['employees', 'a\\b'] },
    { segments: ['employees', 'a\u0000b'] },
  ])('拒绝空、点段、内嵌分隔符与控制字符：$segments', ({ segments }) => {
    expect(buildProxyBackendPath(segments)).toBeUndefined()
  })

  it('拒绝总路径或查询串过长', () => {
    expect(buildProxyBackendPath(Array.from({ length: 5 }, () => 'a'.repeat(1024))))
      .toBeUndefined()
    expect(isValidProxyQuery(`?${'a'.repeat(8191)}`)).toBe(true)
    expect(isValidProxyQuery(`?${'a'.repeat(8192)}`)).toBe(false)
    expect(isValidProxyQuery('?\u0000')).toBe(false)
  })

  it('生产优先内网回源，并拒绝非 origin 形式的环境变量', () => {
    expect(resolveProxyApiBase({
      INTERNAL_API_URL: 'http://api:80',
      NEXT_PUBLIC_API_URL: 'https://public.example.com',
    })).toBe('http://api')
    expect(resolveProxyApiBase({ NEXT_PUBLIC_API_URL: 'https://uat.example.com' }))
      .toBe('https://uat.example.com')
    expect(() => resolveProxyApiBase({ INTERNAL_API_URL: 'ftp://api' })).toThrow()
    expect(() => resolveProxyApiBase({ INTERNAL_API_URL: 'https://user:pass@api' })).toThrow()
    expect(() => resolveProxyApiBase({ INTERNAL_API_URL: 'https://api/internal' })).toThrow()
    expect(() => resolveProxyApiBase({ INTERNAL_API_URL: 'https://api?target=other' })).toThrow()
  })
})

describe('proxy 请求策略', () => {
  it('仅对精确头像 multipart POST 放宽至 2MiB+256KiB', () => {
    const policy = getProxyRequestPolicy('/api/v1/avatars', 'POST', MULTIPART)
    expect(policy.maxBodyBytes).toBe(2 * MIB + 256 * KIB)
    expect(policy.invalidMultipartBoundary).toBe(false)
    expect(policy.forbidden).toBe(false)

    expect(getProxyRequestPolicy('/api/v1/avatars', 'PUT', MULTIPART).maxBodyBytes)
      .toBe(PROXY_DEFAULT_MAX_BODY_BYTES)
    expect(getProxyRequestPolicy('/api/v1/avatars', 'POST', 'application/json').maxBodyBytes)
      .toBe(PROXY_DEFAULT_MAX_BODY_BYTES)
    expect(getProxyRequestPolicy('/api/v1/avatars/extra', 'POST', MULTIPART).maxBodyBytes)
      .toBe(PROXY_DEFAULT_MAX_BODY_BYTES)
  })

  it('精确上传端点的 multipart 缺失或携带非法 boundary 时标记为 400', () => {
    expect(getProxyRequestPolicy(
      '/api/v1/avatars',
      'POST',
      'multipart/form-data',
    ).invalidMultipartBoundary).toBe(true)
    expect(getProxyRequestPolicy(
      '/api/v1/avatars',
      'POST',
      'multipart/form-data; boundary="bad\r\nvalue"',
    ).invalidMultipartBoundary).toBe(true)
  })

  it('同步 ETL 执行保留六分钟响应头窗口，且与人工裁决、回执核验使用相互独立的高风险权限', () => {
    const executePath = `/api/v1/data-pipelines/${GUID}/execute`
    const resolutionPath = `/api/v1/pipeline-executions/${GUID}/resolve-outcome`
    const receiptVerificationPath = `/api/v1/pipeline-executions/${GUID}/verify-target-receipt`

    expect(getProxyRequestPolicy(executePath, 'POST', null).forbidden).toBe(true)
    expect(getProxyRequestPolicy(resolutionPath, 'POST', 'application/json').forbidden).toBe(true)
    expect(getProxyRequestPolicy(receiptVerificationPath, 'POST', null).forbidden).toBe(true)

    const execPolicy = getProxyRequestPolicy(
      executePath,
      'POST',
      'application/json',
      { canExecuteDataPipelines: true },
    )
    expect(execPolicy.maxBodyBytes).toBe(PROXY_DEFAULT_MAX_BODY_BYTES)
    expect(execPolicy.responseHeadersTimeoutMs).toBe(6 * 60_000)
    expect(execPolicy.totalTimeoutMs).toBe(7 * 60_000)
    expect(execPolicy.forbidden).toBe(false)

    expect(getProxyRequestPolicy(
      resolutionPath,
      'POST',
      'application/json',
      { canExecuteDataPipelines: true },
    ).forbidden).toBe(true)
    expect(getProxyRequestPolicy(
      resolutionPath,
      'POST',
      'application/json',
      { canResolvePipelineExecutionOutcome: true },
    )).toMatchObject({
      forbidden: false,
      maxBodyBytes: PROXY_DEFAULT_MAX_BODY_BYTES,
      responseHeadersTimeoutMs: 120_000,
    })
    expect(getProxyRequestPolicy(
      receiptVerificationPath,
      'POST',
      null,
      { canVerifyPipelineExecutionTargetReceipt: true },
    )).toMatchObject({
      forbidden: false,
      maxBodyBytes: PROXY_DEFAULT_MAX_BODY_BYTES,
      responseHeadersTimeoutMs: 120_000,
    })
  })
})

describe('proxy 请求与响应头隔离', () => {
  it('只使用服务端 Bearer，并按方法放行安全协商/Range 头', () => {
    const source = new Headers({
      accept: 'application/octet-stream',
      authorization: 'Bearer browser-token',
      cookie: 'session=secret',
      range: 'bytes=0-1023',
      'if-range': '"etag"',
      'x-internal-secret': 'never-forward',
    })
    const selected = selectProxyRequestHeaders(source, 'GET', 'server-token', 'zh-Hans', false)

    expect(selected.get('authorization')).toBe('Bearer server-token')
    expect(selected.get('accept')).toBe('application/octet-stream')
    expect(selected.get('accept-encoding')).toBe('identity')
    expect(selected.get('accept-language')).toBe('zh-Hans')
    expect(selected.get('range')).toBe('bytes=0-1023')
    expect(selected.get('if-range')).toBe('"etag"')
    expect(selected.get('cookie')).toBeNull()
    expect(selected.get('x-internal-secret')).toBeNull()
  })

  it('响应保留文件/Range 元数据，丢弃 Cookie、重定向、长度与共享缓存策略', () => {
    const selected = selectProxyResponseHeaders(new Headers({
      'content-type': 'application/pdf',
      'content-disposition': 'attachment; filename="report.pdf"',
      'content-length': '999',
      'content-range': 'bytes 0-9/100',
      'cache-control': 'public, s-maxage=3600',
      location: 'https://internal.example/secret',
      'set-cookie': 'secret=1',
      server: 'Kestrel',
      'x-content-type-options': 'nosniff',
    }))

    expect(selected.get('content-type')).toBe('application/pdf')
    expect(selected.get('content-disposition')).toContain('report.pdf')
    expect(selected.get('content-range')).toBe('bytes 0-9/100')
    expect(selected.get('x-content-type-options')).toBe('nosniff')
    expect(selected.get('cache-control')).toBe('private, no-store')
    expect(selected.get('content-length')).toBeNull()
    expect(selected.get('location')).toBeNull()
    expect(selected.get('set-cookie')).toBeNull()
    expect(selected.get('server')).toBeNull()
  })
})

describe('proxy 响应流资源边界', () => {
  it('首块可在第二块产生前读取，且正常结束只 finalize 一次', async () => {
    let releaseSecond: (() => void) | undefined
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array([1, 2]))
        releaseSecond = () => {
          controller.enqueue(new Uint8Array([3, 4]))
          controller.close()
        }
      },
    })
    const onFinalize = vi.fn()
    const stream = streamForwardedResponseBody(source, {
      idleTimeoutMs: 60_000,
      upstreamAbortController: new AbortController(),
      signal: new AbortController().signal,
      onFinalize,
    })
    const reader = stream.getReader()

    expect(Array.from((await reader.read()).value ?? [])).toEqual([1, 2])
    expect(onFinalize).not.toHaveBeenCalled()
    releaseSecond!()
    expect(Array.from((await reader.read()).value ?? [])).toEqual([3, 4])
    expect((await reader.read()).done).toBe(true)
    expect(onFinalize).toHaveBeenCalledTimes(1)
  })
})
