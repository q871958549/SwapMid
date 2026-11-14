'use client'

import { useRef, useState, type RefObject } from 'react'
import { GripVertical, RotateCcw, Undo2 } from 'lucide-react'
import { MotionConfig, Reorder, useDragControls } from 'framer-motion'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Switch } from '@/components/ui/switch'
import { applyDomainOrder, type DomainPrefs } from '@/lib/domain-prefs'
import { translateNavTitle, type NavT, type ResolvedDomain } from '@/lib/nav-utils'
import type { NavDomain } from './nav-config'

interface DomainPrefsDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** 当前账号可见的全部域（内置顺序）。 */
  domains: ResolvedDomain[]
  prefs: DomainPrefs
  /** 返回 false 表示没存成（超长），对话框保持打开。 */
  onSave: (next: DomainPrefs) => boolean
  t: NavT
  ts: NavT
}

function DomainRow({ domain, shown, locked, index, constraintsRef, onToggle, t, ts }: {
  domain: NavDomain
  shown: boolean
  /** 最后一个显示中的域不许关：全关之后切域器与工作台就空了 */
  locked: boolean
  index: number
  /** 拖动范围：列表本身（它也是滚动容器）。 */
  constraintsRef: RefObject<HTMLUListElement | null>
  onToggle: (shown: boolean) => void
  t: NavT
  ts: NavT
}) {
  // 只认把手发起拖拽：整行可拖会和开关的点击、对话框内的触摸滚动抢手势
  const controls = useDragControls()
  const Icon = domain.icon
  const title = translateNavTitle(domain, t)
  return (
    <Reorder.Item
      as="li"
      value={domain.key}
      dragListener={false}
      dragControls={controls}
      // 钉在列表可视区内：列表是滚动容器，拖出去的那一行会被裁掉、看不见（2026-11-05 用户反馈）。
      // 拖到边缘时 Reorder 自带的边缘自动滚动会把列表滚过来。dragElastic 0：越界的回弹拉伸照样会被裁。
      dragConstraints={constraintsRef}
      dragElastic={0}
      // 不放大：滚动容器里 scale 会撑出横向溢出，冒出横向滚动条、两侧被裁。提起感交给阴影。
      // zIndex 必须写进 whileDrag（Reorder.Item 内联 style 会盖过 className 的 z-*）
      whileDrag={{ zIndex: 30, boxShadow: 'var(--shadow-md)', cursor: 'grabbing' }}
      className="flex items-center gap-2 rounded-md border bg-background px-2 py-1.5"
    >
      <span
        aria-hidden
        onPointerDown={(e) => controls.start(e)}
        className="cursor-grab touch-none rounded p-1 text-muted-foreground/60 hover:text-foreground"
      >
        <GripVertical className="size-4" />
      </span>
      <Icon className={cn('size-4 shrink-0', shown ? 'text-primary' : 'text-muted-foreground/50')} />
      <span className={cn('min-w-0 flex-1 truncate text-sm', !shown && 'text-muted-foreground')}>{title}</span>
      {/* 快捷键只编号「显示」的域，与 Alt+数字的实际映射一致 */}
      {shown && index < 9 && (
        <kbd className="shrink-0 rounded border px-1 font-mono text-[10px] text-muted-foreground">Alt+{index + 1}</kbd>
      )}
      <Switch checked={shown} disabled={locked} onCheckedChange={onToggle} aria-label={`${ts('showDomain')}${title}`} />
    </Reorder.Item>
  )
}

interface Draft {
  order: string[]
  hidden: string[]
}

function sameDraft(a: Draft, b: Draft): boolean {
  return a.order.join() === b.order.join() && [...a.hidden].sort().join() === [...b.hidden].sort().join()
}

/** 「管理业务域」对话框（蓝图 P1-1）：拖动排序 + 开关显示，点保存才落库。 */
export function DomainPrefsDialog({ open, onOpenChange, domains, prefs, onSave, t, ts }: DomainPrefsDialogProps) {
  const defaultKeys = domains.map((d) => d.domain.key)
  const [draft, setDraft] = useState<Draft>({ order: [], hidden: [] })
  // 「恢复默认」之前的草稿。恢复只改草稿、不落库，但一键抹掉拖了半天的顺序仍然吓人（2026-11-05 用户反馈）：
  // 恢复后按钮就地变成「撤销恢复」，再动任何一项才失效。
  const [beforeRestore, setBeforeRestore] = useState<Draft | null>(null)
  const listRef = useRef<HTMLUListElement>(null)
  // 每次打开都从已保存的偏好重新初始化（render-adjust，取消后再打开不残留上次的拖动）
  const [wasOpen, setWasOpen] = useState(false)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) {
      setDraft({
        order: applyDomainOrder(defaultKeys, prefs.order, (key) => key),
        hidden: prefs.hidden.filter((key) => defaultKeys.includes(key)),
      })
      setBeforeRestore(null)
    }
  }

  const { order, hidden } = draft
  const byKey = new Map(domains.map((d) => [d.domain.key, d.domain]))
  const shownKeys = order.filter((key) => !hidden.includes(key))
  const defaultDraft: Draft = { order: defaultKeys, hidden: [] }
  const isDefault = sameDraft(draft, defaultDraft)

  function edit(next: Draft) {
    setDraft(next)
    setBeforeRestore(null)
  }

  function save() {
    if (onSave(draft)) onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{ts('manageDomains')}</DialogTitle>
          <DialogDescription>{ts('manageDomainsDescription')}</DialogDescription>
        </DialogHeader>
        <MotionConfig reducedMotion="user">
          <Reorder.Group
            ref={listRef}
            as="ul"
            axis="y"
            values={order}
            onReorder={(next) => edit({ order: next, hidden })}
            // overflow-x-hidden：纵向滚动容器的 overflow-x 会被算成 auto，任何横向溢出都会冒滚动条；
            // -mx-1 px-1 py-1：给行的边框与拖起时的阴影留出不被裁的余量，视觉宽度不变。
            // select-none：拖动时指针扫过的文字不再被选中、蓝底高亮。
            className="-mx-1 max-h-[60vh] space-y-1.5 overflow-x-hidden overflow-y-auto px-1 py-1 select-none"
          >
            {order.map((key) => {
              const domain = byKey.get(key)
              if (!domain) return null
              return (
                <DomainRow
                  key={key}
                  domain={domain}
                  shown={!hidden.includes(key)}
                  locked={shownKeys.length === 1 && shownKeys[0] === key}
                  index={shownKeys.indexOf(key)}
                  constraintsRef={listRef}
                  onToggle={(shown) => edit({ order, hidden: shown ? hidden.filter((k) => k !== key) : [...hidden, key] })}
                  t={t}
                  ts={ts}
                />
              )
            })}
          </Reorder.Group>
        </MotionConfig>
        <DialogFooter className="sm:justify-between">
          {beforeRestore ? (
            <Button variant="ghost" onClick={() => { setDraft(beforeRestore); setBeforeRestore(null) }}>
              <Undo2 />
              {ts('undoRestoreDomainDefaults')}
            </Button>
          ) : (
            <Button
              variant="ghost"
              disabled={isDefault}
              onClick={() => { setBeforeRestore(draft); setDraft(defaultDraft) }}
            >
              <RotateCcw />
              {ts('restoreDomainDefaults')}
            </Button>
          )}
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => onOpenChange(false)}>{ts('cancel')}</Button>
            <Button onClick={save}>{ts('save')}</Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
