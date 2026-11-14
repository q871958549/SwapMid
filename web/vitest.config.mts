import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'

// web 单元测试配置：jsdom 环境跑 React 19 组件 / hook 测试。
// 覆盖 CRUD 共享件（29 个列表页共用）与自定义 ESLint 架构规则；不碰 Next 运行时
//（Server Component / 路由 / Route Handler）——那些走 e2e/手测。
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // 复刻 tsconfig 的 `@/* → src/*`（vitest 不读 tsconfig paths，需在此重声明）。
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}', 'eslint-rules/**/*.test.mjs'],
    // 共享件与 ESLint 规则测试不依赖样式表，跳过 CSS 处理（Tailwind v4 PostCSS 不进测试链路）。
    css: false,
    // 每个用例后自动清 mock 调用记录、还原 vi.stubGlobal（fetch 桩不跨用例泄漏）。
    clearMocks: true,
    unstubGlobals: true,
  },
})
