/**
 * 路由内容的进场层（2026-08-07「缺乏呼吸感」打磨）。
 *
 * **为什么是 template 而不是 layout**：App Router 的 layout 在导航之间**不重新挂载**，挂在它上面的
 * 进场动画只会在首次加载播一次；template 每次导航都重挂子树，正是官方给「每次导航都要重播」这类
 * 需求留的出口。改动前页签切换是内容硬切，是「割裂 / 生硬」的来源之一。
 *
 * **为什么把滚动容器整体下移到这里**（而不是在 `<main>` 里再套一个中性动画层）：
 * `data-scroll-container` + `p-6` + overflow 全部随层下移后，与改动前**完全同构**——滚动容器自带
 * 内距，长页尾距、列表页那条 flex 高度链都逐像素等价。若只在 main 内插一层，就得额外验证「长内容
 * 溢出中间层之后 padding-bottom 还在不在」这类浏览器边界行为，白白背上验证负担。
 *
 * ⚠️ 两条已核对的约束，改这里前先看：
 * - `data-scroll-container` 全站只有 `ScrollRestorer` 一个消费方（+其测试）。它的 effect 依赖
 *   pathname、每次导航重新 `querySelector`，所以容器随导航重建天然兼容（新容器 scrollTop=0，
 *   恢复逻辑照旧）。
 * - `<main>` 语义标签仍保留在 layout 的外壳上，a11y 无损。
 *
 * 动画本身在 `globals.css` 的 `.swapmid-page-enter`（150ms，与页签切换同拍，带 reduced-motion 降级）。
 */
export default function DashboardTemplate({ children }: { children: React.ReactNode }) {
  return (
    <div
      data-scroll-container
      // p-4 md:p-6：窄屏（手机）24px 内距会吃掉 360px 里的 48px，收到 16px；md 起与改动前逐像素一致
      className="swapmid-page-enter flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-none p-4 md:p-6 [overflow-anchor:none]"
    >
      {children}
    </div>
  )
}
