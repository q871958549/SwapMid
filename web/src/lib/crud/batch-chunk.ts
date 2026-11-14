'use client'

/**
 * 批量操作的分片提交器（全站共享；纯函数 + 常量，不 import 任何业务文件）。
 *
 * ⚠️ 顶部的 `'use client'` 不可删：本文件直接 `fetch('/api/proxy/...')`，是**客户端**代码路径
 * （经 Route Handler 代理转发，而非服务端 `apiFetch` 直连后端）。少了它会被架构门禁
 * `server-backend-boundary-contract` 判为「未声明客户端边界却直接 fetch 的生产模块」而拦下
 * ——那道门禁守的是「服务端回源必须走受审查的有界请求内核」，本文件不属于那条链路。
 *
 * ## 为什么需要它
 *
 * 后端给批量写入契约设了硬上限（`BatchOperationLimits.MaxIdsPerRequest = 2000`），超出**整批拒绝**
 * ——静默截断会让用户以为全部完成，是比报错糟得多的失败形态。而列表支持跨页勾选、每页档位用户可调
 * （服务端分页上限 5000），「把每页调到 5000 再点全选」是一次完全正常的 UI 操作，一下就能选出
 * 5000 个 id。**没有分片的话，那一次点击必然撞 400。**
 *
 * 🔴 **正解是前端分片，不是调大后端上限**：上限护的是**单个 HTTP 请求的时长**——批量删除逐条走
 * 完整校验与关联检查，几千条就是数千次数据库往返、秒级到十秒级的单请求，调大只会把它拖到分钟级
 * 并撞代理超时。分片后每个请求仍是可控时长，用户看到的是进度而不是报错。
 *
 * ## 语义变化（调用方必须让用户看见）
 *
 * 超过一片时，「整批一个请求」变成「分段提交」：中途失败，**已完成的片不回滚**。
 * 这与批量操作本就逐条报成败的语义一致（后端从来不承诺整批原子），但用户需要被明确告知——
 * 所以进度要可见、结果要把各片合并后完整呈现。
 *
 * ## 沿革
 *
 * 本文件源自样品域的 `project-management/samples/batch-chunk.ts`（模块内私有）。2026-08-04 随
 * 「共享批量契约加上限」一并提升为 `lib/crud` 共享件——样品域那份继续保留（它的分片大小锚的是
 * `SampleBatchAppService.MaxBatchSize = 200`，与本文件锚的 2000 是两个不同的后端约束，不能合并）。
 */

/**
 * 单片条数。**必须 ≤ 后端 `BatchOperationLimits.MaxIdsPerRequest`**（当前 2000）。
 * 取等值即可：小于它只是多跑几趟请求（无害），大于它则每片都被整批拒绝。
 * ⚠️ 跨层契约，改动须与后端常量同步，两处注释互指。
 */
export const BATCH_CHUNK_SIZE = 2000

/** 分片提交进度。`done` 是已提交条数（不是片数），便于直接显示「x / y 条」。 */
export interface BatchChunkProgress {
  done: number
  total: number
  chunkIndex: number
  chunkCount: number
}

/** 后端 `BatchOperationResultDto` 的前端镜像（全部批量端点共用同一形状）。 */
export interface BatchOperationResult {
  requested: number
  succeeded: number
  skipped: number
  messages: string[]
}

/**
 * 把 `ids` 切成 `chunkSize` 一片**顺序**提交，返回各片结果数组（聚合口径由调用方决定）。
 *
 * - **顺序而非并发**：并发几片会同时在后端跑数千条的写事务，争锁与连接池占用都不划算，
 *   且进度对用户不再是单调的。
 * - `signal` 已中止时**在片之间**停下（不打断进行中的那一片——它在后端已经在写了，
 *   强行放弃只会让前端不知道那一片的结果）。已完成的片保留，由调用方呈现。
 * - 某片抛错则整体抛出，调用方需同时呈现「已完成的片」——这正是分段提交与单请求的差别所在。
 */
export async function submitInChunks<T>(
  ids: string[],
  send: (chunk: string[]) => Promise<T>,
  options?: {
    chunkSize?: number
    signal?: AbortSignal
    onProgress?: (progress: BatchChunkProgress) => void
  },
): Promise<T[]> {
  const chunkSize = options?.chunkSize ?? BATCH_CHUNK_SIZE
  const chunks: string[][] = []
  for (let i = 0; i < ids.length; i += chunkSize) {
    chunks.push(ids.slice(i, i + chunkSize))
  }

  const results: T[] = []
  let done = 0
  for (const [index, chunk] of chunks.entries()) {
    if (options?.signal?.aborted) break
    options?.onProgress?.({
      done,
      total: ids.length,
      chunkIndex: index + 1,
      chunkCount: chunks.length,
    })
    results.push(await send(chunk))
    done += chunk.length
  }
  options?.onProgress?.({
    done,
    total: ids.length,
    chunkIndex: chunks.length,
    chunkCount: chunks.length,
  })
  return results
}

/**
 * 批量端点的**一行接入**：分片 POST 到 `/api/proxy/{endpoint}`，并把各片的
 * `BatchOperationResultDto` 合并成一个。
 *
 * 页面原本写的是一次裸 `fetch`，换成本函数即自动获得分片能力——**这是 12 个批删页面接入的正门**，
 * 不要各自再写一遍分片循环（那样每页都要重新处理进度、中止与结果合并，必然长出不一致）。
 *
 * @param endpoint 不含 `/api/proxy/` 前缀，如 `positions/batch-delete`
 * @param ids 待处理 id（可跨页累计，长度不限——超过一片自动分段）
 * @param options.extraBody 端点特有的额外字段（如 `{ isEnabled: true }` / `{ status: 1 }`），
 *   会原样并入**每一片**的请求体
 */
export async function submitBatchIds(
  endpoint: string,
  ids: string[],
  options?: {
    extraBody?: Record<string, unknown>
    chunkSize?: number
    signal?: AbortSignal
    onProgress?: (progress: BatchChunkProgress) => void
  },
): Promise<BatchOperationResult> {
  const parts = await submitInChunks<BatchOperationResult>(
    ids,
    async (chunk) => {
      const res = await fetch(`/api/proxy/${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...options?.extraBody, ids: chunk }),
      })
      if (!res.ok) throw new Error(await res.text())
      return (await res.json()) as BatchOperationResult
    },
    options,
  )

  // 合并：计数相加、消息拼接。**不去重**——各片 id 互不相交，后端又是按传入 id 逐条产出的，
  // 拼接后天然无重复；去重反而会掩盖「同一 id 出现两次」这种真该暴露的异常。
  return {
    requested: parts.reduce((sum, p) => sum + (p.requested ?? 0), 0),
    succeeded: parts.reduce((sum, p) => sum + (p.succeeded ?? 0), 0),
    skipped: parts.reduce((sum, p) => sum + (p.skipped ?? 0), 0),
    messages: parts.flatMap((p) => p.messages ?? []),
  }
}
