import { useEffect, useState } from 'react'

const NARROW_QUERY = '(max-width: 767px)'

/**
 * `md` 以下为窄屏。首帧固定 false，与服务端骨架一致，挂载后再读 matchMedia，
 * 避免首帧结构和 hydration 不一致。测试环境的 matchMedia 默认不匹配，列表单测仍走桌面表。
 */
export function useNarrowScreen(): boolean {
  const [narrow, setNarrow] = useState(false)

  useEffect(() => {
    const media = window.matchMedia(NARROW_QUERY)
    const apply = () => setNarrow(media.matches)
    apply()
    media.addEventListener('change', apply)
    return () => media.removeEventListener('change', apply)
  }, [])

  return narrow
}
