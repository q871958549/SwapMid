import { useEffect, useRef } from 'react'

/**
 * 服务端搜索防抖 emit：搜索框输入经 300ms 防抖后回调父组件取数。
 *
 * 仅 manualPagination（服务端分页）时生效；挂载首帧不触发（避免无谓重取）。
 * 抽离动机：这段「用 ref 存最新回调 + 首帧跳过 + 防抖 timer」的样板独立于表格其余状态，
 * 收进 hook 让主组件的 effect 列表更短、意图更聚焦。
 */
export function useDebouncedSearchEmit(
  globalFilter: string,
  manualPagination: boolean,
  onSearchChange: ((value: string) => void) | undefined,
) {
  // 用 ref 存最新回调，避免把 onSearchChange 放进防抖 effect 的依赖——
  // 父组件每次渲染传新函数会打断防抖、令 timer 反复重建。
  const onSearchChangeRef = useRef(onSearchChange)
  useEffect(() => { onSearchChangeRef.current = onSearchChange }, [onSearchChange])

  const searchEmitInitRef = useRef(true)
  useEffect(() => {
    if (!manualPagination) return
    if (searchEmitInitRef.current) {
      searchEmitInitRef.current = false
      return
    }
    const timer = setTimeout(() => onSearchChangeRef.current?.(globalFilter), 300)
    return () => clearTimeout(timer)
  }, [globalFilter, manualPagination])
}
