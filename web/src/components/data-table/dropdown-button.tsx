'use client'

import * as React from 'react'
import { ChevronDown } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'

interface DropdownButtonProps {
  /** 触发按钮文案。纯图标场景可省略，此时必须传 `ariaLabel`。 */
  label?: React.ReactNode
  /** 触发按钮前置图标（如 `<Upload />`），与 `label`/箭头共享同一 Button 的 flex gap，无需自带 margin。 */
  icon?: React.ReactNode
  /** 下拉内容——通常是一个或多个 `<DropdownMenuItem>`。 */
  items: React.ReactNode
  variant?: React.ComponentProps<typeof Button>['variant']
  size?: React.ComponentProps<typeof Button>['size']
  /** 浮层对齐，默认 `'end'`（贴触发器右边缘）。浮层易被视口左边缘截断的场景（如选中行浮动工具条最左侧按钮）可传 `'start'`。 */
  align?: 'start' | 'center' | 'end'
  disabled?: boolean
  /** 嵌在 Dialog 内时必须 `false`，避免双 modal 误关外层对话框。 */
  modal?: boolean
  /**
   * 无障碍名（`aria-label`）。未传 `label`（纯图标触发器）时必填；已传 `label` 时可选——
   * 不传则读屏用可见文案（常见场景，多数调用方无需传）,传入则覆盖，用于可见文案较短、
   * 读屏需要更明确描述的场景（如可见「操作」+ `ariaLabel="项目操作"` 消歧）。
   */
  ariaLabel?: string
  /** 触发按钮额外类名（如 `h-7` 对齐同排其他紧凑按钮）。 */
  className?: string
  /** `DropdownMenuContent` 额外类名。 */
  contentClassName?: string
}

/**
 * 「纯菜单按钮」共享原语——下拉按钮/菜单触发器三分型（详见 `web/AGENTS.md`「下拉按钮 / 菜单
 * 触发器规范」）之 **C 型**：一按钮点开一组平级选项、整个按钮都是点击热区、无独立可直点的主操作
 * （如「导入 ▾」展开 CSV/Excel 两条导入路径、「批量状态 ▾」展开恢复在职/停职/离职）。
 *
 * 统一 token：文字/图标按钮 + 右侧固定 `ChevronDown`（`size="sm"` 时 `size-3.5`，其余走 Button
 * 全局默认 `size-4`，与工具栏其余图标一致）；浮层默认 `align="end"` + `min-w` 对齐触发器宽度
 * （短选项菜单不会窄于按钮本身）。危险项自行在传入的 `items` 里加 `className="text-destructive"`
 * 或 `<DropdownMenuItem variant="destructive">`。
 *
 * 与另外两型的边界——**不要**用本组件顶替它们：
 * - **A 行操作**（表格一行的编辑/删除等）→ `actionsColumn`（`column-factories.tsx`，纯图标
 *   `⋯` MoreHorizontal 触发器）；行操作过于复杂（多动作 + 联动 disabled + 条件渲染）无法套工厂时，
 *   手写 `DropdownMenu` + `MoreHorizontal` 仍是认可的逃生舱，同样不套本组件。
 * - **B split button**（一个主操作 + 多个互斥变体，主操作可直接点，左右两个独立热区）→ 当前全站零实例
 *   （表单内「保存并打印标签」原为此型，因是单个非互斥增强动作，已改用 `CrudFormDialog.submitExtra`
 *   并排按钮）；页头/工具栏若出现真正的多互斥变体实例，抽 `SplitButton`。
 *
 * @example 「导入 ▾」——展开两条平级导入路径，各项带副标题说明
 * <DropdownButton icon={<Upload />} label="导入" items={<>
 *   <DropdownMenuItem asChild>
 *     <Link href="/data-integration/data-sources">
 *       <div className="flex flex-col">
 *         <span>从 CSV 导入</span>
 *         <span className="text-xs text-muted-foreground">按模板文件批量登记</span>
 *       </div>
 *     </Link>
 *   </DropdownMenuItem>
 * </>} />
 *
 * @example 选中行浮动工具条内的「批量流转 ▾」——贴视口左边缘，用 align="start"
 * <DropdownButton icon={<Send />} label="批量流转" align="start" items={<>
 *   <DropdownMenuItem onClick={...}>批量领用</DropdownMenuItem>
 * </>} />
 */
export function DropdownButton({
  label,
  icon,
  items,
  variant = 'outline',
  size = 'sm',
  align = 'end',
  disabled,
  modal,
  ariaLabel,
  className,
  contentClassName,
}: DropdownButtonProps) {
  return (
    <DropdownMenu modal={modal}>
      <DropdownMenuTrigger asChild>
        <Button
          variant={variant}
          size={size}
          disabled={disabled}
          aria-label={ariaLabel}
          className={className}
        >
          {icon}
          {label}
          <ChevronDown className={size === 'sm' ? 'size-3.5' : undefined} />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align={align}
        className={cn('min-w-[var(--radix-dropdown-menu-trigger-width)]', contentClassName)}
      >
        {items}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
