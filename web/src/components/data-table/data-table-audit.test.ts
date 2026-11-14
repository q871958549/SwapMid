import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ColumnDef } from '@tanstack/react-table'
import { loadAuditUsers, withAuditColumns } from './data-table-audit'

function response(body: unknown, init: { ok?: boolean; status?: number } = {}): Response {
  return {
    ok: init.ok ?? true,
    status: init.status ?? 200,
    json: vi.fn().mockResolvedValue(body),
  } as unknown as Response
}

function guid(index: number) {
  return `00000000-0000-0000-0000-${index.toString(16).padStart(12, '0')}`
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('loadAuditUsers', () => {
  it('只提交去重后的指定 ID，并缓存成功结果', async () => {
    const id = guid(1)
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(response({
      items: [{ id, displayName: '张三', userName: 'zhangsan' }],
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(loadAuditUsers([id, id], 'identity-deduplicate')).resolves.toEqual({
      [id]: { id, name: '张三', userName: 'zhangsan' },
    })
    await loadAuditUsers([id], 'identity-deduplicate')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body))).toEqual({ ids: [id] })
  })

  it('混合大小写 Guid 与 lower-case 缓存使用同一身份键', async () => {
    const lowerId = 'abcdefab-cdef-abcd-efab-cdefabcdefab'
    const mixedId = 'ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB'
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(response({
      items: [{ id: mixedId, displayName: '大小写用户' }],
    }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(loadAuditUsers([mixedId], 'identity-guid-case')).resolves.toEqual({
      [lowerId]: { id: lowerId, name: '大小写用户' },
    })
    await loadAuditUsers([lowerId], 'identity-guid-case')

    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('HTTP 失败不做负缓存，下一次调用会重试', async () => {
    const id = guid(2)
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(response({}, { ok: false, status: 503 }))
      .mockResolvedValueOnce(response({ items: [{ id, displayName: '李四' }] }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(loadAuditUsers([id], 'identity-retry')).resolves.toEqual({})
    await expect(loadAuditUsers([id], 'identity-retry')).resolves.toEqual({
      [id]: { id, name: '李四' },
    })
    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('重叠 ID 的并发调用共享同一个在途批次', async () => {
    const id = guid(3)
    let resolveRequest!: (value: Response) => void
    const pending = new Promise<Response>((resolve) => { resolveRequest = resolve })
    const fetchMock = vi.fn<typeof fetch>().mockReturnValue(pending)
    vi.stubGlobal('fetch', fetchMock)

    const first = loadAuditUsers([id], 'identity-single-flight')
    const second = loadAuditUsers([id], 'identity-single-flight')
    resolveRequest(response({ items: [{ id, displayName: '王五' }] }))

    await expect(Promise.all([first, second])).resolves.toEqual([
      { [id]: { id, name: '王五' } },
      { [id]: { id, name: '王五' } },
    ])
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('身份切换后旧请求迟到也不能写入新世代缓存', async () => {
    const id = guid(4)
    let resolveOld!: (value: Response) => void
    const oldRequest = new Promise<Response>((resolve) => { resolveOld = resolve })
    const fetchMock = vi.fn<typeof fetch>()
      .mockReturnValueOnce(oldRequest)
      .mockResolvedValueOnce(response({ items: [{ id, displayName: '新身份姓名' }] }))
    vi.stubGlobal('fetch', fetchMock)

    const oldLoad = loadAuditUsers([id], 'identity-old')
    await expect(loadAuditUsers([id], 'identity-new')).resolves.toEqual({
      [id]: { id, name: '新身份姓名' },
    })
    resolveOld(response({ items: [{ id, displayName: '旧身份姓名' }] }))
    await expect(oldLoad).resolves.toEqual({})
    await expect(loadAuditUsers([id], 'identity-new')).resolves.toEqual({
      [id]: { id, name: '新身份姓名' },
    })

    expect(fetchMock).toHaveBeenCalledTimes(2)
  })

  it('超过服务端单批上限时完整分批，不做静默截断', async () => {
    const ids = Array.from({ length: 2001 }, (_, index) => guid(index + 100))
    const batchSizes: number[] = []
    let active = 0
    let maxActive = 0
    const fetchMock = vi.fn<typeof fetch>(async (_input, init) => {
      active += 1
      maxActive = Math.max(maxActive, active)
      const body = JSON.parse(String(init?.body)) as { ids: string[] }
      batchSizes.push(body.ids.length)
      await Promise.resolve()
      active -= 1
      return response({ items: body.ids.map((id) => ({ id, displayName: id })) })
    })
    vi.stubGlobal('fetch', fetchMock)

    const result = await loadAuditUsers(ids, 'identity-batches')

    expect(batchSizes.sort((a, b) => b - a)).toEqual([2000, 1])
    expect(maxActive).toBeLessThanOrEqual(2)
    expect(Object.keys(result)).toHaveLength(ids.length)
  })
})

describe('withAuditColumns 排序边界守卫', () => {
  type SampleRow = {
    id: string
    name: string
    creationTime: string
    creatorId: string
  }

  const sampleData: SampleRow[] = [
    { id: '1', name: 'A', creationTime: '2026-08-01T10:00:00Z', creatorId: guid(10) },
  ]
  const t = (key: string) => key

  it('客户端分页且有可排业务列时，审计时间列与审计人列均可前端排序', () => {
    const cols: ColumnDef<SampleRow, unknown>[] = [{ accessorKey: 'name' }]
    const result = withAuditColumns(cols, sampleData, true, {}, false, t, 'zh-CN', false, false)
    const creationCol = result.find((c) => 'accessorKey' in c && c.accessorKey === 'creationTime')
    const creatorCol = result.find((c) => c.id === 'creator')

    expect(creationCol?.enableSorting).toBe(true)
    expect(creatorCol?.enableSorting).toBe(true)
  })

  it('纯服务端分页未开启 manualSorting 时，禁止审计时间列与审计人列仅排当前页的假排序', () => {
    const cols: ColumnDef<SampleRow, unknown>[] = [{ accessorKey: 'name' }]
    const result = withAuditColumns(cols, sampleData, true, {}, false, t, 'zh-CN', false, true)
    const creationCol = result.find((c) => 'accessorKey' in c && c.accessorKey === 'creationTime')
    const creatorCol = result.find((c) => c.id === 'creator')

    expect(creationCol?.enableSorting).toBe(false)
    expect(creatorCol?.enableSorting).toBe(false)
  })

  it('服务端排序开启且业务列可排时，审计时间列保留排序、审计人派生列禁排', () => {
    const cols: ColumnDef<SampleRow, unknown>[] = [{ accessorKey: 'name' }]
    const result = withAuditColumns(cols, sampleData, true, {}, false, t, 'zh-CN', true, true)
    const creationCol = result.find((c) => 'accessorKey' in c && c.accessorKey === 'creationTime')
    const creatorCol = result.find((c) => c.id === 'creator')

    expect(creationCol?.enableSorting).toBe(true)
    expect(creatorCol?.enableSorting).toBe(false)
  })

  it('全部业务列均显式 enableSorting: false（固定顺序账本）时，自动注入的审计列一律禁排', () => {
    const cols: ColumnDef<SampleRow, unknown>[] = [{ accessorKey: 'name', enableSorting: false }]
    const result = withAuditColumns(cols, sampleData, true, {}, false, t, 'zh-CN', true, true)
    const creationCol = result.find((c) => 'accessorKey' in c && c.accessorKey === 'creationTime')
    const creatorCol = result.find((c) => c.id === 'creator')

    expect(creationCol?.enableSorting).toBe(false)
    expect(creatorCol?.enableSorting).toBe(false)
  })
})
