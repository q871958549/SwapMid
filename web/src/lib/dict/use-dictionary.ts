'use client'

import { useState, useEffect, useCallback } from 'react'
import { DICTIONARY_CHANGED_EVENT } from './dictionary-events'

export interface DictItem {
  value: string
  label: string
  sortOrder: number
}

interface RawDictItem {
  value: string
  label: string
  sortOrder: number
}

/**
 * 数据字典消费 hook：按字典编码加载启用的字典项（经 /api/proxy 调后端
 * `GET /data-dictionaries/by-code/{code}/items`），供业务页下拉选项与
 * 「值 → 显示文本」翻译复用。这是数据字典面向全平台的统一消费入口。
 *
 * 用法：
 *   const { items, translate } = useDictionary('order_status')
 *   // 下拉：items.map(i => <SelectItem value={i.value}>{i.label}</SelectItem>)
 *   // 翻译：translate(row.status)  // '1' → '已完成'
 */
export function useDictionary(code: string | undefined) {
  const [items, setItems] = useState<DictItem[]>([])

  // effect body 不同步调用 setState（setItems 在 await 之后执行），
  // 规避 react-hooks/set-state-in-effect。
  useEffect(() => {
    if (!code) return
    let active = true
    let request = 0
    let controller: AbortController | undefined
    const reload = async () => {
      const current = ++request
      controller?.abort()
      controller = new AbortController()
      try {
        const res = await fetch(`/api/proxy/data-dictionaries/by-code/${encodeURIComponent(code)}/items`, { signal: controller.signal, cache: 'no-store' })
        if (!res.ok) throw new Error(await res.text())
        const data = (await res.json()) as { items?: RawDictItem[] }
        if (active && current === request) {
          setItems((data.items ?? []).map(i => ({ value: i.value, label: i.label, sortOrder: i.sortOrder })))
        }
      } catch {
        if (active && current === request) setItems([])
      }
    }
    const changed = (event: Event) => {
      if ((event as CustomEvent<string>).detail === code) void reload()
    }
    const refresh = () => { void reload() }
    const visible = () => { if (document.visibilityState === 'visible') void reload() }
    void reload()
    window.addEventListener(DICTIONARY_CHANGED_EVENT, changed)
    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', visible)
    return () => {
      active = false
      controller?.abort()
      window.removeEventListener(DICTIONARY_CHANGED_EVENT, changed)
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', visible)
    }
  }, [code])

  /** 值 → 显示文本；查不到时回退原值（避免显示空白）。 */
  const translate = useCallback(
    (value: string | null | undefined) => {
      if (value == null || value === '') return ''
      return items.find(i => i.value === value)?.label ?? value
    },
    [items],
  )

  return { items, translate }
}
