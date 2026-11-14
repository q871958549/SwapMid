import { describe, it, expect, vi, beforeEach } from 'vitest'
import { BATCH_CHUNK_SIZE, submitBatchIds, submitInChunks } from './batch-chunk'

/**
 * 批量分片提交器的回归网。
 *
 * 它被 12 个批删页面共用，且守的是一条**用户一键就能撞上**的边界：后端批量契约上限 2000，
 * 而列表每页档位可调到 5000、跨页勾选还能再累加——没有分片，一次全选就是 400。
 */

function okResponse(body: unknown) {
  return { ok: true, json: async () => body } as Response
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('submitBatchIds · 分片与合并', () => {
  it('不足一片时只发一次请求，请求体带全部 id', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      okResponse({ requested: 3, succeeded: 3, skipped: 0, messages: [] }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const result = await submitBatchIds('positions/batch-delete', ['a', 'b', 'c'])

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][0]).toBe('/api/proxy/positions/batch-delete')
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ ids: ['a', 'b', 'c'] })
    expect(result.succeeded).toBe(3)
  })

  /**
   * 🔴 这条是本文件的核心：后端上限 2000，用户把每页调到 5000 再全选就是 5000 个 id。
   * 没有分片 = 一次 400。断言锚 `BATCH_CHUNK_SIZE` 而非字面量，改上限时用例自动跟随。
   */
  it('超过一片时按 BATCH_CHUNK_SIZE 分段提交，且每片都不超上限', async () => {
    const fetchMock = vi.fn().mockImplementation(async (_url, init) => {
      const ids = JSON.parse(init.body).ids as string[]
      return okResponse({ requested: ids.length, succeeded: ids.length, skipped: 0, messages: [] })
    })
    vi.stubGlobal('fetch', fetchMock)

    const ids = Array.from({ length: BATCH_CHUNK_SIZE * 2 + 5 }, (_, i) => `id-${i}`)
    const result = await submitBatchIds('employees/batch-delete', ids)

    expect(fetchMock).toHaveBeenCalledTimes(3)
    for (const call of fetchMock.mock.calls) {
      expect(JSON.parse(call[1].body).ids.length).toBeLessThanOrEqual(BATCH_CHUNK_SIZE)
    }
    // 合并后的计数必须等于总数——分片是实现细节，调用方看到的应与「一次提交」无差别。
    expect(result.requested).toBe(ids.length)
    expect(result.succeeded).toBe(ids.length)
  })

  it('各片的计数相加、消息拼接', async () => {
    let call = 0
    const fetchMock = vi.fn().mockImplementation(async () => {
      call += 1
      return okResponse({
        requested: 2, succeeded: 1, skipped: 1, messages: [`第 ${call} 片有跳过`],
      })
    })
    vi.stubGlobal('fetch', fetchMock)

    const ids = Array.from({ length: BATCH_CHUNK_SIZE + 1 }, (_, i) => `id-${i}`)
    const result = await submitBatchIds('rooms/batch-delete', ids)

    expect(result.succeeded).toBe(2)
    expect(result.skipped).toBe(2)
    expect(result.messages).toEqual(['第 1 片有跳过', '第 2 片有跳过'])
  })

  /** extraBody 必须并入**每一片**——只并进第一片会让后续片丢掉 status/isEnabled 等关键字段。 */
  it('extraBody 并入每一片的请求体', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      okResponse({ requested: 1, succeeded: 1, skipped: 0, messages: [] }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const ids = Array.from({ length: BATCH_CHUNK_SIZE + 1 }, (_, i) => `id-${i}`)
    await submitBatchIds('employees/batch-set-status', ids, { extraBody: { status: 2 } })

    expect(fetchMock).toHaveBeenCalledTimes(2)
    for (const call of fetchMock.mock.calls) {
      expect(JSON.parse(call[1].body).status).toBe(2)
    }
  })

  it('某片失败时整体抛出，错误带后端响应文本', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, text: async () => '越权' } as Response)
    vi.stubGlobal('fetch', fetchMock)

    await expect(submitBatchIds('positions/batch-delete', ['a'])).rejects.toThrow('越权')
  })
})

describe('submitInChunks · 进度与中止', () => {
  it('进度按已提交条数递增，末次回调 done 等于总数', async () => {
    const progress: number[] = []
    const ids = Array.from({ length: 5 }, (_, i) => `id-${i}`)

    await submitInChunks(ids, async () => 'ok', {
      chunkSize: 2,
      onProgress: (p) => progress.push(p.done),
    })

    // 3 片 → 提交前各回调一次（0/2/4）+ 结束时一次（5）
    expect(progress).toEqual([0, 2, 4, 5])
  })

  /**
   * 中止只在**片之间**生效：进行中的那一片在后端已经在写了，强行放弃只会让前端不知道它的结果。
   * 这里预先中止，故一片都不该发出。
   */
  it('signal 已中止时不再提交后续片', async () => {
    const send = vi.fn().mockResolvedValue('ok')
    const controller = new AbortController()
    controller.abort()

    const results = await submitInChunks(['a', 'b', 'c'], send, {
      chunkSize: 1,
      signal: controller.signal,
    })

    expect(send).not.toHaveBeenCalled()
    expect(results).toEqual([])
  })

  it('分片大小可覆盖，切分不丢不重', async () => {
    const sent: string[][] = []
    const ids = ['a', 'b', 'c', 'd', 'e']

    await submitInChunks(ids, async (chunk) => { sent.push(chunk); return chunk.length }, { chunkSize: 2 })

    expect(sent).toEqual([['a', 'b'], ['c', 'd'], ['e']])
    expect(sent.flat()).toEqual(ids)
  })
})
