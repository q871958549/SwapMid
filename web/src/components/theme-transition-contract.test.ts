import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const src = (...parts: string[]) => readFileSync(join(process.cwd(), 'src', ...parts), 'utf8')

/**
 * 锁住主题切换「统一染色」不变量。录像实证：外壳 dark:bg-zinc-* 先跳、
 * 子孙 transition-colors 再追一条时间轴，150ms ease-out 只剩两三次中间态。
 */
describe('theme transition contract', () => {
  it('工作台壳走 --canvas，不再用 dark:zinc 硬色', () => {
    const layout = src('app', '(dashboard)', 'layout.tsx')
    expect(layout).toContain('bg-canvas')
    expect(layout).toContain('h-dvh')
    expect(layout).not.toMatch(/dark:bg-zinc-/)
  })

  it('色板注册 --canvas，短命窗口 200ms ease-in-out 且掐掉子孙过渡', () => {
    const css = src('app', 'globals.css')
    expect(css).toMatch(/@property --canvas \{[^}]*inherits: true/)
    expect(css).toMatch(/html\.theme-transitioning \{[\s\S]*--canvas/)
    expect(css).toMatch(/html\.theme-transitioning \{[\s\S]*transition-duration:\s*200ms/)
    expect(css).toMatch(/html\.theme-transitioning \{[\s\S]*ease-in-out/)
    expect(css).toMatch(/html\.theme-transitioning \*\s*\{\s*transition:\s*none/)
  })

  it('JS 卸类时长远与 CSS 200ms 同拍', () => {
    const provider = src('components', 'theme-provider.tsx')
    expect(provider).toMatch(/THEME_TRANSITION_MS\s*=\s*200/)
  })

  it('触屏与窄屏不挂 theme-transitioning', () => {
    const provider = src('components', 'theme-provider.tsx')
    expect(provider).toMatch(/export function shouldAnimateThemeTransition/)
    expect(provider).toContain('(pointer: coarse)')
    expect(provider).toContain('innerWidth < 768')
    expect(provider).toContain('if (shouldAnimateThemeTransition())')
  })

  it('登录页入场动画不得在 remount 时从 scale 0 / opacity 0 重播', () => {
    const login = src('app', '(auth)', 'login', 'client.tsx')
    expect(login).toContain('initial={false}')
    expect(login).not.toMatch(/initial=\{\{\s*scale:\s*0/)
    expect(login).toContain('min-h-dvh')
  })

  it('表头分隔线用 var(--border)，不用 hsl() 包 oklch token', () => {
    const grid = src('components', 'data-table', 'data-table-grid.tsx')
    expect(grid).toContain('shadow-[0_1px_0_0_var(--border)]')
    expect(grid).not.toContain('hsl(var(--border))')
  })
})
