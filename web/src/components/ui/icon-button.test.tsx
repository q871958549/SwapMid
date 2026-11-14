import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { IconButton } from './icon-button'

// 占位图标：lucide 图标在测试里就是个 svg；这里用带 testid 的 span 代替，只验 children 被渲染。
function Icon() {
  return <span data-testid="icon" />
}

describe('IconButton', () => {
  it('label 同时作为无障碍名（aria-label）：可按名查到按钮', () => {
    render(<IconButton label="删除"><Icon /></IconButton>)
    expect(screen.getByRole('button', { name: '删除' })).toBeInTheDocument()
  })

  it('渲染 children 图标', () => {
    render(<IconButton label="删除"><Icon /></IconButton>)
    expect(screen.getByTestId('icon')).toBeInTheDocument()
  })

  it('点击触发 onClick', () => {
    const onClick = vi.fn()
    render(<IconButton label="删除" onClick={onClick}><Icon /></IconButton>)
    fireEvent.click(screen.getByRole('button', { name: '删除' }))
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('disabled 透传：禁用时不触发 onClick', () => {
    const onClick = vi.fn()
    render(<IconButton label="删除" disabled onClick={onClick}><Icon /></IconButton>)
    const btn = screen.getByRole('button', { name: '删除' })
    expect(btn).toBeDisabled()
    fireEvent.click(btn)
    expect(onClick).not.toHaveBeenCalled()
  })

  it('showTooltip=false 时仍保留带 aria-label 的按钮', () => {
    render(<IconButton label="删除" showTooltip={false}><Icon /></IconButton>)
    expect(screen.getByRole('button', { name: '删除' })).toBeInTheDocument()
  })

  it('透传 Button 的 variant / className', () => {
    render(<IconButton label="删除" variant="outline" className="text-destructive"><Icon /></IconButton>)
    expect(screen.getByRole('button', { name: '删除' }).className).toContain('text-destructive')
  })
})
