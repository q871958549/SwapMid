import { afterEach, describe, expect, it } from 'vitest'
import { getApiErrorMessage, parseOptionalJsonResponse, readApiErrorMessage } from '@/lib/api-error'
import {
  resetPlatformUpdatingForTests,
  setPlatformUpdatingCopy,
} from '@/lib/platform-updating'

afterEach(() => {
  resetPlatformUpdatingForTests()
})

describe('getApiErrorMessage', () => {
  it('上传表单截断时给出可操作提示，不回显底层英文异常', () => {
    const error = new Error(JSON.stringify({ error: {
      message: '您的请求无效！',
      details: 'Failed to read the request form. Unexpected end of Stream, the content may have already been read by another component.',
    } }))
    expect(getApiErrorMessage(error, 'fallback', { incompleteUpload: 'upload-incomplete' }))
      .toBe('upload-incomplete')
    expect(getApiErrorMessage(error, 'fallback')).toBe('fallback')
  })

  it.each(['您的请求无效！', 'Your request is not valid!'])('通用校验摘要优先展示具体字段错误：%s', message => {
    expect(getApiErrorMessage(new Error(JSON.stringify({ error: {
      message,
      details: 'Validation failed',
      validationErrors: [{ message: '请选择印章' }, { message: '请输入日期' }],
    } })), 'fallback')).toBe('请选择印章；请输入日期')
  })

  it('保留具体业务错误摘要', () => {
    expect(getApiErrorMessage(new Error(JSON.stringify({ error: {
      message: '文件超过50MB上限', validationErrors: [{ message: 'other' }],
    } })), 'fallback')).toBe('文件超过50MB上限')
  })

  it('解出 ABP error.message', () => {
    expect(getApiErrorMessage(
      new Error(JSON.stringify({ error: { message: '指标编号已存在' } })),
      '兜底',
    )).toBe('指标编号已存在')
  })

  it('笼统服务器内部错误时改用 details', () => {
    expect(getApiErrorMessage(
      new Error(JSON.stringify({
        error: {
          message: '\u670d\u52a1\u5668\u5185\u90e8\u9519\u8bef',
          details: '23505 duplicate key IX_AppPerformanceIndicators_IndicatorNo',
        },
      })),
      '兜底',
    )).toBe('23505 duplicate key IX_AppPerformanceIndicators_IndicatorNo')
  })

  it('发布窗口 503 使用更新文案，而不是 Service Unavailable', () => {
    setPlatformUpdatingCopy({
      title: 'updating-title',
      description: 'updating-description',
    })
    expect(getApiErrorMessage(
      new Error(JSON.stringify({ error: 'Service Unavailable', code: 'PLATFORM_UPDATING' })),
      '兜底',
    )).toBe('updating-description')
  })

  it('代理字符串 Gateway Timeout 使用调用方兜底，避免只显示请稍后重试的空语义', () => {
    expect(getApiErrorMessage(
      new Error(JSON.stringify({ error: 'Gateway Timeout' })),
      '请稍后刷新查看是否已生成',
    )).toBe('请稍后刷新查看是否已生成')
  })

  it('非协议类字符串 error 原样返回', () => {
    expect(getApiErrorMessage(
      new Error(JSON.stringify({ error: '上游会话已过期' })),
      '兜底',
    )).toBe('上游会话已过期')
  })

  it('从上游 Result.ResponseStatus 抽出字段不存在短句', () => {
    const upstreamPayload = {
      Result: {
        ResponseStatus: {
          IsSuccess: false,
          Errors: [{ FieldName: null, Message: '字段“FPickMtrlQty”不存在' }],
        },
      },
    }
    expect(getApiErrorMessage(
      new Error(JSON.stringify({ error: { message: `上游接口调用失败：${JSON.stringify(upstreamPayload)}。目标记录未落库` } })),
      '兜底',
    )).toBe('字段“FPickMtrlQty”不存在')
  })
})

describe('readApiErrorMessage / parseOptionalJsonResponse', () => {
  it('从 403 Response JSON 正文中解出业务提示，而非返回 [object Response]', async () => {
    const res = new Response(
      JSON.stringify({ error: { code: 'SwapMid:ETL:036', message: '检测到上游 Schema 漂移' } }),
      { status: 403, headers: { 'Content-Type': 'application/json' } },
    )
    await expect(readApiErrorMessage(res, '兜底')).resolves.toBe('检测到上游 Schema 漂移')
  })

  it('204 No Content 与空字符串响应安全返回 undefined', async () => {
    await expect(parseOptionalJsonResponse(new Response(null, { status: 204 }))).resolves.toBeUndefined()
    await expect(parseOptionalJsonResponse(new Response('', { status: 200 }))).resolves.toBeUndefined()
    await expect(parseOptionalJsonResponse(new Response('{"ok":true}', { status: 200 }))).resolves.toEqual({ ok: true })
  })
})
