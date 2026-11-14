'use client'

import { useCallback, useRef, useState } from 'react'

interface LazyRefRequest<T> {
  key: symbol
  path: string
  promise: Promise<T[]>
  /** null 表示仍在途；成功后写入惰性过期时间。 */
  expiresAt: number | null
}

const LAZY_REF_TTL_MS = 5 * 60 * 1000

function readItems<T>(value: unknown): T[] {
  if (!value || typeof value !== 'object' || !Array.isArray((value as { items?: unknown }).items)) {
    throw new TypeError('Lazy reference response must contain an items array')
  }
  return (value as { items: T[] }).items
}

/**
 * 弹窗参照数据的懒加载：首屏 Server Component 不再预取这些（企业/部门/岗位等），
 * 而是在第一次需要时（打开新建/编辑弹窗）经 Route Handler 代理拉取，且只拉一次。
 *
 * - `items`：用于渲染（state，加载后触发更新）
 * - `ensure()`：触发加载并返回该次加载结果（可在打开弹窗时 await 拿到，用于设默认值）
 *
 * @param path 后端相对路径（不含 /api/proxy/ 前缀），如 `enterprises?maxResultCount=1000`
 */
export function useLazyRef<T>(path: string) {
  const [items, setItems] = useState<T[]>([])
  const requestRef = useRef<LazyRefRequest<T> | null>(null)

  const invalidate = useCallback(() => {
    // 保留 last-known-good 供当前已打开的表单继续展示，但让下一次 ensure 强制重取。
    // 同时撤销在途请求的写回资格，避免写操作完成前发出的旧快照随后覆盖新数据。
    requestRef.current = null
  }, [])

  const ensure = useCallback((): Promise<T[]> => {
    const current = requestRef.current
    if (
      current?.path === path
      && (current.expiresAt === null || current.expiresAt > Date.now())
    ) return current.promise

    if (current && current.path !== path) {
      // 动态 path 切换时旧列表不再属于当前参照；等待新请求期间先清空，避免误选旧数据。
      setItems([])
    }

    const key = Symbol(path)
    const promise = fetch(`/api/proxy/${path}`)
      .then(async (res) => {
        if (!res.ok) throw new Error(`Lazy reference request failed with status ${res.status}`)
        return readItems<T>(await res.json())
      })
      .then((list) => {
        // path 已切换时旧请求即使晚到，也不得覆盖新参照的 state。
        if (requestRef.current?.key === key) {
          requestRef.current.expiresAt = Date.now() + LAZY_REF_TTL_MS
          setItems(list)
        }
        return list
      })
      .catch(() => {
        // 网络异常、HTTP 非 2xx 与响应结构异常都不得缓存为空成功；下次打开允许重试。
        if (requestRef.current?.key === key) requestRef.current = null
        return [] as T[]
      })

    requestRef.current = { key, path, promise, expiresAt: null }
    return promise
  }, [path])

  return { items, ensure, invalidate }
}
