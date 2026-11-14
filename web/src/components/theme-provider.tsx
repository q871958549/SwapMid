'use client'

import { createContext, useContext, useEffect, useState } from 'react'

type Theme = 'light' | 'dark' | 'system'
type ResolvedTheme = 'light' | 'dark'

interface ThemeContextValue {
  theme: Theme
  setTheme: (theme: Theme) => void
  resolvedTheme: ResolvedTheme
  fontSize: number
  setFontSize: (fontSize: number) => void
}

const ThemeContext = createContext<ThemeContextValue>({
  theme: 'system',
  setTheme: () => {},
  resolvedTheme: 'light',
  fontSize: 16,
  setFontSize: () => {},
})

const STORAGE_KEY = 'swapmid_theme'
const FONT_SIZE_STORAGE_KEY = 'swapmid_font_size'
const MIN_FONT_SIZE = 13
const MAX_FONT_SIZE = 18
const COOKIE_MAX_AGE = 365 * 24 * 60 * 60

function clampFontSize(value: number): number {
  return Math.min(MAX_FONT_SIZE, Math.max(MIN_FONT_SIZE, value))
}

function getSystemTheme(): ResolvedTheme {
  if (typeof window === 'undefined') return 'light'
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function getStoredTheme(): Theme {
  if (typeof window === 'undefined') return 'system'
  try { return (localStorage.getItem(STORAGE_KEY) as Theme) || 'system' } catch { return 'system' }
}

function getStoredFontSize(): number {
  if (typeof window === 'undefined') return 16
  try {
    const raw = localStorage.getItem(FONT_SIZE_STORAGE_KEY)
    return raw ? clampFontSize(Number(raw)) : 16
  } catch {
    return 16
  }
}

function resolve(t: Theme): ResolvedTheme {
  return t === 'system' ? getSystemTheme() : t
}

// ---- 主题切换过渡（配套 globals.css 的 html.theme-transitioning）----
// 仅用户主动切主题 / system 模式下 OS 偏好变化时挂短命类；首屏 theme-init 脚本与
// hydration 的 class 对齐不经这里，保持瞬时上色（FOUC 防护不受影响）。
// 时长与 CSS 侧 200ms 同拍，+50ms 缓冲防边缘掐尾；连点时清旧 timer 重新计时。
// 200ms ease-in-out 是「全页反相」对快档 150ms 的例外：ΔL≈220，150ms ease-out
// 只会留下两三次能看清的中间态，外壳再一跳就散成分层变色。
const THEME_TRANSITION_CLASS = 'theme-transitioning'
const THEME_TRANSITION_MS = 200
let themeTransitionTimer: ReturnType<typeof setTimeout> | undefined

/** 桌面精细指针才走 200ms token 插值。触屏 / 窄屏瞬时切换：
 *  ① @property 插值中途标题与背景同暗/同亮，字会「隐身」；
 *  ② 登录页 login-bg 的 swapmid-fade-in 从 opacity 0 播，品牌区空一拍；
 *  ③ Android WebView 改 color-scheme 会整层丢合成，暗→亮闪白页。 */
export function shouldAnimateThemeTransition(): boolean {
  if (typeof window === 'undefined') return false
  try {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return false
    if (window.matchMedia('(hover: none), (pointer: coarse)').matches) return false
    if (window.innerWidth < 768) return false
  } catch {
    return false
  }
  return true
}

function applyResolvedTheme(resolved: ResolvedTheme) {
  const root = document.documentElement
  const isDark = resolved === 'dark'
  if (root.classList.contains('dark') === isDark) return
  if (shouldAnimateThemeTransition()) {
    root.classList.add(THEME_TRANSITION_CLASS)
    if (themeTransitionTimer) clearTimeout(themeTransitionTimer)
    themeTransitionTimer = setTimeout(() => {
      root.classList.remove(THEME_TRANSITION_CLASS)
      themeTransitionTimer = undefined
    }, THEME_TRANSITION_MS + 50)
  }
  root.classList.toggle('dark', isDark)
}

function writeCookie(name: string, value: string) {
  try {
    document.cookie = `${name}=${value}; path=/; max-age=${COOKIE_MAX_AGE}; samesite=lax`
  } catch {}
}

/**
 * 落库当前用户界面偏好；未登录 / 失败静默忽略（localStorage + cookie 已兜底）。
 * ThemeProvider 挂在根 layout（含登录页），未登录切主题会 401，此处 catch 吞掉。
 */
function persistPreference(body: Record<string, unknown>) {
  fetch('/api/proxy/api/v1/user-preference', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  }).catch(() => {})
}

export function ThemeProvider({
  children,
  initialTheme,
  initialFontSize,
}: {
  children: React.ReactNode
  /** 服务端从 swapmid_theme cookie（= 账号偏好镜像）读到的初值；登录用户首屏即正确、无闪白。 */
  initialTheme?: Theme
  initialFontSize?: number
}) {
  const [theme, setThemeState] = useState<Theme>(() => initialTheme ?? getStoredTheme())
  const [resolvedTheme, setResolvedTheme] = useState<ResolvedTheme>(() => resolve(initialTheme ?? getStoredTheme()))
  const [fontSize, setFontSizeState] = useState<number>(() => initialFontSize ?? getStoredFontSize())

  // 服务端初值（cookie=账号偏好）落回 localStorage，保持二者一致——否则 system 模式的 OS 监听
  // 会因 localStorage 为空（换设备首登）而把账号选定的浅/深色误判成 system 并被 OS 覆盖。
  useEffect(() => {
    if (initialTheme) { try { localStorage.setItem(STORAGE_KEY, initialTheme) } catch {} }
    if (initialFontSize) { try { localStorage.setItem(FONT_SIZE_STORAGE_KEY, String(initialFontSize)) } catch {} }
  }, [initialTheme, initialFontSize])

  useEffect(() => {
    document.documentElement.style.fontSize = `${fontSize}px`
  }, [fontSize])

  // Sync with OS preference when in 'system' mode
  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const handler = () => {
      if (getStoredTheme() === 'system') {
        const resolved = getSystemTheme()
        setResolvedTheme(resolved)
        applyResolvedTheme(resolved)
      }
    }
    mq.addEventListener('change', handler)
    return () => mq.removeEventListener('change', handler)
  }, [])

  function setTheme(t: Theme) {
    try { localStorage.setItem(STORAGE_KEY, t) } catch {}
    writeCookie(STORAGE_KEY, t)
    const resolved = resolve(t)
    setThemeState(t)
    setResolvedTheme(resolved)
    applyResolvedTheme(resolved)
    persistPreference({ theme: t })
  }

  function setFontSize(value: number) {
    const next = clampFontSize(value)
    try { localStorage.setItem(FONT_SIZE_STORAGE_KEY, String(next)) } catch {}
    writeCookie(FONT_SIZE_STORAGE_KEY, String(next))
    setFontSizeState(next)
    document.documentElement.style.fontSize = `${next}px`
    persistPreference({ fontSize: next })
  }

  return (
    <ThemeContext.Provider value={{ theme, setTheme, resolvedTheme, fontSize, setFontSize }}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext)
}
