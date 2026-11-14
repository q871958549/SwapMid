import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Toast, ToastProvider, ToastTitle, ToastViewport } from './toast'

describe('Toast variants', () => {
  it('destructive 使用实底，禁止半透明红底叠字', () => {
    render(
      <ToastProvider>
        <Toast open variant="destructive">
          <ToastTitle>请先限定统计日期</ToastTitle>
        </Toast>
        <ToastViewport />
      </ToastProvider>
    )

    const root = screen.getByText('请先限定统计日期').closest('[data-state]')
    expect(root).toBeTruthy()
    expect(root).toHaveClass('bg-red-50')
    expect(root?.className).not.toMatch(/bg-destructive\/5/)
  })

  it('default / success 保持实底', () => {
    const { rerender } = render(
      <ToastProvider>
        <Toast open variant="default">
          <ToastTitle>已保存</ToastTitle>
        </Toast>
        <ToastViewport />
      </ToastProvider>
    )
    expect(screen.getByText('已保存').closest('[data-state]')).toHaveClass('bg-background')

    rerender(
      <ToastProvider>
        <Toast open variant="success">
          <ToastTitle>已保存</ToastTitle>
        </Toast>
        <ToastViewport />
      </ToastProvider>
    )
    expect(screen.getByText('已保存').closest('[data-state]')).toHaveClass('bg-success-soft')
  })
})
