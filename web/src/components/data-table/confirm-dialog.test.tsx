import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { ConfirmDialog } from './confirm-dialog'
import zhHansMessages from '../../../messages/zh-Hans.json'

// ConfirmDialog 内部经 useTranslations('Common') 求默认按钮文案（确认/取消/处理中...，
// i18n 第二期批次2b）——组件树需要 NextIntlClientProvider 才能渲染。真实 zh-Hans messages 直供。
function renderDialog(
  props: {
    open?: boolean
    busy?: boolean
    destructive?: boolean
    confirmText?: string
    cancelText?: string
    busyText?: string
  } = {},
) {
  const onConfirm = vi.fn()
  const onOpenChange = vi.fn()
  render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <ConfirmDialog
        open={props.open ?? true}
        onOpenChange={onOpenChange}
        title="确认删除岗位"
        description="将永久删除岗位「测试」，此操作无法撤销。"
        onConfirm={onConfirm}
        busy={props.busy}
        destructive={props.destructive}
        confirmText={props.confirmText}
        cancelText={props.cancelText}
        busyText={props.busyText}
      />
    </NextIntlClientProvider>,
  )
  return { onConfirm, onOpenChange }
}

describe('ConfirmDialog', () => {
  it('open=true 渲染 title 与 description', () => {
    renderDialog()
    expect(screen.getByText('确认删除岗位')).toBeInTheDocument()
    expect(screen.getByText('将永久删除岗位「测试」，此操作无法撤销。')).toBeInTheDocument()
  })

  it('open=false 不渲染内容', () => {
    renderDialog({ open: false })
    expect(screen.queryByText('确认删除岗位')).not.toBeInTheDocument()
  })

  it('confirmText / cancelText 默认为「确认」「取消」', () => {
    renderDialog()
    expect(screen.getByRole('button', { name: '确认' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '取消' })).toBeInTheDocument()
  })

  it('busy=true 时禁用两个按钮、确认显示 busyText', () => {
    renderDialog({ busy: true, confirmText: '删除', busyText: '删除中...' })
    expect(screen.getByRole('button', { name: '删除中...' })).toBeDisabled()
    expect(screen.getByRole('button', { name: '取消' })).toBeDisabled()
  })

  it('destructive=true 时确认按钮带 destructive 配色类', () => {
    renderDialog({ destructive: true, confirmText: '删除' })
    expect(screen.getByRole('button', { name: '删除' }).className).toContain('bg-destructive')
  })

  it('点确认调用 onConfirm，并 preventDefault（避免对话框抢先关闭）', () => {
    const { onConfirm } = renderDialog({ confirmText: '删除' })
    const notPrevented = fireEvent.click(screen.getByRole('button', { name: '删除' }))
    expect(onConfirm).toHaveBeenCalledTimes(1)
    // fireEvent.click 返回 false 表示事件 defaultPrevented 被调用
    expect(notPrevented).toBe(false)
  })

  it('点取消调用 onOpenChange(false)', () => {
    const { onOpenChange } = renderDialog()
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('busy=true 时确认按钮禁用，点击不触发 onConfirm', () => {
    const { onConfirm } = renderDialog({ busy: true, confirmText: '删除', busyText: '删除中...' })
    fireEvent.click(screen.getByRole('button', { name: '删除中...' }))
    expect(onConfirm).not.toHaveBeenCalled()
  })
})
