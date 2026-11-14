'use client'

import { useState, useSyncExternalStore } from 'react'
import { useTranslations } from 'next-intl'
import { Pipette } from 'lucide-react'
import { IconButton } from '@/components/ui/icon-button'
import { Input } from '@/components/ui/input'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Slider } from '@/components/ui/slider'
import { cn } from '@/lib/utils'

// —— 纯转换函数（导出供单测）——

export interface Hsv {
  /** 色相 0–360 */
  h: number
  /** 饱和度 0–1 */
  s: number
  /** 明度 0–1 */
  v: number
}

/** 归一化 hex：可带可不带 #，6 位十六进制 → 大写无 #；其余返回 null。 */
export function normalizeHex(input: string): string | null {
  const m = /^#?([0-9a-fA-F]{6})$/.exec(input.trim())
  return m ? m[1].toUpperCase() : null
}

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const n = normalizeHex(hex) ?? '000000'
  return {
    r: parseInt(n.slice(0, 2), 16),
    g: parseInt(n.slice(2, 4), 16),
    b: parseInt(n.slice(4, 6), 16),
  }
}

export function rgbToHex(r: number, g: number, b: number): string {
  const c = (x: number) =>
    Math.min(255, Math.max(0, Math.round(x)))
      .toString(16)
      .padStart(2, '0')
  return `${c(r)}${c(g)}${c(b)}`.toUpperCase()
}

export function hexToHsv(hex: string): Hsv {
  const { r, g, b } = hexToRgb(hex)
  const rn = r / 255
  const gn = g / 255
  const bn = b / 255
  const max = Math.max(rn, gn, bn)
  const min = Math.min(rn, gn, bn)
  const d = max - min
  let h = 0
  if (d > 0) {
    if (max === rn) h = 60 * (((gn - bn) / d) % 6)
    else if (max === gn) h = 60 * ((bn - rn) / d + 2)
    else h = 60 * ((rn - gn) / d + 4)
  }
  if (h < 0) h += 360
  return { h, s: max === 0 ? 0 : d / max, v: max }
}

export function hsvToHex({ h, s, v }: Hsv): string {
  const c = v * s
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const m = v - c
  const [r, g, b] =
    h < 60 ? [c, x, 0]
    : h < 120 ? [x, c, 0]
    : h < 180 ? [0, c, x]
    : h < 240 ? [0, x, c]
    : h < 300 ? [x, 0, c]
    : [c, 0, x]
  return rgbToHex((r + m) * 255, (g + m) * 255, (b + m) * 255)
}

const clamp01 = (n: number) => Math.min(1, Math.max(0, n))

const subscribeNoop = () => () => {}
function getEyeDropperSnapshot(): boolean {
  return typeof window !== 'undefined' && 'EyeDropper' in window
}

// EyeDropper API（Chrome/Edge）；TS lib 未收录，最小声明
interface EyeDropperCtor {
  new (): { open(): Promise<{ sRGBHex: string }> }
}

interface ColorPickerProps {
  /** 触发按钮的 id，供外部 <Label htmlFor> 关联。 */
  id?: string
  /** 当前颜色 hex（可带 #、大小写不限）。非法值时保持上一次有效色显示。 */
  value: string
  /** 只回传合法值：6 位大写无 #。 */
  onChange: (hex: string) => void
  disabled?: boolean
  className?: string
}

/**
 * 简洁取色器：饱和度/明度面板 + 色相滑条 + hex / RGB 双通道输入 + 屏幕拾取（EyeDropper 可用时）。
 *
 * 内部以 HSV 为工作态（保证饱和度为 0 时拖色相不丢方向），hex 是对外契约。
 * 外部 value 变化经 render-adjust 同步（React 官方模式），无 effect。
 */
export function ColorPicker({ id, value, onChange, disabled, className }: ColorPickerProps) {
  const t = useTranslations('Common.colorPicker')
  const [syncedHex, setSyncedHex] = useState(() => normalizeHex(value) ?? '000000')
  const [hsv, setHsv] = useState<Hsv>(() => hexToHsv(normalizeHex(value) ?? '000000'))
  const [hexDraft, setHexDraft] = useState<string | null>(null)
  const [rgbDraft, setRgbDraft] = useState<Partial<Record<'r' | 'g' | 'b', string>>>({})

  // 外部 value 变化时同步内部工作态（render-adjust；syncedHex 作 guard 防循环）
  const incoming = normalizeHex(value)
  if (incoming && incoming !== syncedHex) {
    setSyncedHex(incoming)
    setHsv(hexToHsv(incoming))
  }

  /** 内部各输入通道统一出口：更新工作态并上抛。传 nextHsv 可保留无彩色下的色相方向。 */
  function commitHex(hex: string, nextHsv?: Hsv) {
    setSyncedHex(hex)
    setHsv(nextHsv ?? hexToHsv(hex))
    onChange(hex)
  }

  function applyHsv(next: Hsv) {
    commitHex(hsvToHex(next), next)
  }

  function updateFromPointer(e: React.PointerEvent<HTMLDivElement>) {
    const rect = e.currentTarget.getBoundingClientRect()
    if (rect.width <= 0 || rect.height <= 0) return
    applyHsv({
      h: hsv.h,
      s: clamp01((e.clientX - rect.left) / rect.width),
      v: clamp01(1 - (e.clientY - rect.top) / rect.height),
    })
  }

  function onPanelKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    const delta = e.shiftKey ? 0.1 : 0.02
    let { s, v } = hsv
    if (e.key === 'ArrowLeft') s -= delta
    else if (e.key === 'ArrowRight') s += delta
    else if (e.key === 'ArrowUp') v += delta
    else if (e.key === 'ArrowDown') v -= delta
    else return
    e.preventDefault()
    applyHsv({ h: hsv.h, s: clamp01(s), v: clamp01(v) })
  }

  function setRgbChannel(ch: 'r' | 'g' | 'b', raw: string) {
    setRgbDraft(d => ({ ...d, [ch]: raw }))
    if (raw.trim() === '') return
    const n = Number(raw)
    if (!Number.isInteger(n) || n < 0 || n > 255) return
    const next = { ...hexToRgb(syncedHex), [ch]: n }
    const hex = rgbToHex(next.r, next.g, next.b)
    if (hex !== syncedHex) commitHex(hex)
  }

  async function pickFromScreen() {
    const ctor = (window as unknown as { EyeDropper?: EyeDropperCtor }).EyeDropper
    if (!ctor) return
    try {
      const result = await new ctor().open()
      const n = normalizeHex(result.sRGBHex)
      if (n && n !== syncedHex) commitHex(n)
    } catch {
      // 用户按 Esc 取消拾取，属正常路径
    }
  }

  const rgb = hexToRgb(syncedHex)
  const hueCss = `hsl(${Math.round(hsv.h)}, 100%, 50%)`
  // 服务端快照恒 false：EyeDropper 只在 Chromium 有，render 期读 window 会 hydration mismatch
  const supportsEyeDropper = useSyncExternalStore(subscribeNoop, getEyeDropperSnapshot, () => false)

  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          id={id}
          disabled={disabled}
          className={cn(
            'border-input flex h-9 w-full items-center gap-2 rounded-md border bg-transparent px-3 py-1 text-sm shadow-xs transition-[color,box-shadow] outline-none select-none',
            'focus-visible:border-ring focus-visible:ring-(--focus-ring-color) focus-visible:ring-(length:--focus-ring-width) ring-inset',
            'disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50',
            className,
          )}
        >
          <span
            aria-hidden
            data-slot="color-swatch"
            className="size-5 shrink-0 rounded-sm border"
            style={{ backgroundColor: `#${syncedHex}` }}
          />
          <span className="font-mono">#{syncedHex}</span>
        </button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-64 space-y-3 p-3">
        <div
          role="slider"
          aria-label={t('svPanel')}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(hsv.v * 100)}
          aria-valuetext={t('svValueText', {
            s: Math.round(hsv.s * 100),
            v: Math.round(hsv.v * 100),
          })}
          tabIndex={0}
          className="relative h-36 w-full cursor-crosshair touch-none rounded-md border select-none focus-visible:border-ring focus-visible:ring-(length:--focus-ring-width) focus-visible:ring-(--focus-ring-color) focus-visible:outline-none"
          style={{
            background: `linear-gradient(to top, #000, transparent), linear-gradient(to right, #fff, ${hueCss})`,
          }}
          onPointerDown={e => {
            e.preventDefault()
            e.currentTarget.setPointerCapture(e.pointerId)
            e.currentTarget.focus()
            updateFromPointer(e)
          }}
          onPointerMove={e => {
            if (e.buttons & 1) updateFromPointer(e)
          }}
          onKeyDown={onPanelKeyDown}
        >
          <span
            aria-hidden
            className="pointer-events-none absolute size-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white shadow-sm"
            style={{
              left: `${hsv.s * 100}%`,
              top: `${(1 - hsv.v) * 100}%`,
              backgroundColor: `#${syncedHex}`,
            }}
          />
        </div>

        <div className="relative">
          <div
            aria-hidden
            className="absolute top-1/2 h-1.5 w-full -translate-y-1/2 rounded-full"
            style={{
              background:
                'linear-gradient(to right, #f00 0%, #ff0 17%, #0f0 33%, #0ff 50%, #00f 67%, #f0f 83%, #f00 100%)',
            }}
          />
          <Slider
            min={0}
            max={360}
            step={1}
            value={[Math.round(hsv.h)]}
            thumbLabel={t('hue')}
            onValueChange={([h]) => applyHsv({ ...hsv, h })}
            className="[&_[data-slot=slider-range]]:bg-transparent [&_[data-slot=slider-track]]:bg-transparent"
          />
        </div>

        <div className="flex items-center gap-2">
          <Input
            aria-label="Hex"
            value={hexDraft ?? syncedHex}
            maxLength={7}
            onFocus={() => setHexDraft(syncedHex)}
            onChange={e => {
              setHexDraft(e.target.value)
              const n = normalizeHex(e.target.value)
              if (n && n !== syncedHex) commitHex(n)
            }}
            onBlur={() => setHexDraft(null)}
            className="h-8 flex-1 font-mono"
          />
          {supportsEyeDropper && (
            <IconButton
              label={t('eyedropper')}
              className="size-8 shrink-0"
              variant="outline"
              onClick={() => void pickFromScreen()}
            >
              <Pipette />
            </IconButton>
          )}
        </div>

        <div className="grid grid-cols-3 gap-2">
          {(['r', 'g', 'b'] as const).map(ch => (
            <div key={ch} className="flex items-center gap-1.5">
              <span aria-hidden className="text-muted-foreground w-3 text-xs">
                {ch.toUpperCase()}
              </span>
              <Input
                aria-label={ch.toUpperCase()}
                type="number"
                min={0}
                max={255}
                value={rgbDraft[ch] ?? String(rgb[ch])}
                onChange={e => setRgbChannel(ch, e.target.value)}
                onBlur={() => setRgbDraft({})}
                className="h-8 tabular-nums"
              />
            </div>
          ))}
        </div>
      </PopoverContent>
    </Popover>
  )
}
