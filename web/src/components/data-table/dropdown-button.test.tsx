import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { DropdownMenuItem } from '@/components/ui/dropdown-menu'
import { DropdownButton } from './dropdown-button'

// Radix DropdownMenu 的 Trigger 靠 onPointerDown 打开，jsdom 下 fireEvent.click 不触发；
// 用 Enter 打开（与 column-factories.test.tsx / crud-form-dialog.test.tsx 一致）。
function openMenu(name: string) {
  fireEvent.keyDown(screen.getByRole('button', { name }), { key: 'Enter' })
}

describe('DropdownButton', () => {
  it('渲染触发按钮：label 文案 + 图标 + 默认箭头', () => {
    render(
      <DropdownButton
        label="导入"
        icon={<svg data-testid="icon-upload" />}
        items={<DropdownMenuItem>从 CSV 导入</DropdownMenuItem>}
      />,
    )
    const btn = screen.getByRole('button', { name: '导入' })
    expect(btn).toBeInTheDocument()
    expect(screen.getByTestId('icon-upload')).toBeInTheDocument()
  })

  it('未展开时不渲染菜单项', () => {
    render(<DropdownButton label="导入" items={<DropdownMenuItem>从 CSV 导入</DropdownMenuItem>} />)
    expect(screen.queryByRole('menuitem', { name: '从 CSV 导入' })).not.toBeInTheDocument()
  })

  it('点击触发器展开 items 传入的菜单项', async () => {
    render(
      <DropdownButton
        label="导入"
        items={<>
          <DropdownMenuItem>从 CSV 导入</DropdownMenuItem>
          <DropdownMenuItem>从 Excel 导入</DropdownMenuItem>
        </>}
      />,
    )
    openMenu('导入')
    expect(await screen.findByRole('menuitem', { name: '从 CSV 导入' })).toBeInTheDocument()
    expect(await screen.findByRole('menuitem', { name: '从 Excel 导入' })).toBeInTheDocument()
  })

  it('点击菜单项调用调用方自己的 onClick', async () => {
    const onClick = vi.fn()
    render(
      <DropdownButton
        label="导出"
        items={<DropdownMenuItem onClick={onClick}>导出当日</DropdownMenuItem>}
      />,
    )
    openMenu('导出')
    fireEvent.click(await screen.findByRole('menuitem', { name: '导出当日' }))
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('disabled=true 时触发按钮禁用', () => {
    render(<DropdownButton label="批量状态" disabled items={<DropdownMenuItem>停职</DropdownMenuItem>} />)
    expect(screen.getByRole('button', { name: '批量状态' })).toBeDisabled()
  })

  it('disabled 未传（undefined）时触发按钮可点', () => {
    render(<DropdownButton label="批量状态" items={<DropdownMenuItem>停职</DropdownMenuItem>} />)
    expect(screen.getByRole('button', { name: '批量状态' })).toBeEnabled()
  })

  it('未传 label 时用 ariaLabel 兜底可访问名', () => {
    render(<DropdownButton ariaLabel="更多操作" items={<DropdownMenuItem>详情</DropdownMenuItem>} />)
    expect(screen.getByRole('button', { name: '更多操作' })).toBeInTheDocument()
  })

  it('label 与 ariaLabel 同传时，ariaLabel 覆盖可见文案作为无障碍名（可见「操作」+ 读屏「项目操作」消歧场景）', () => {
    render(
      <DropdownButton label="操作" ariaLabel="项目操作" items={<DropdownMenuItem>删除项目</DropdownMenuItem>} />,
    )
    expect(screen.getByRole('button', { name: '项目操作' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '项目操作' })).toHaveTextContent('操作')
  })

  it('align 默认 "end"（透传给 DropdownMenuContent）', async () => {
    render(<DropdownButton label="导入" items={<DropdownMenuItem>从 CSV 导入</DropdownMenuItem>} />)
    openMenu('导入')
    expect((await screen.findByRole('menuitem', { name: '从 CSV 导入' })).closest('[data-align]'))
      .toHaveAttribute('data-align', 'end')
  })

  it('align 可覆盖为 "start"（选中行浮动工具条最左侧按钮场景）', async () => {
    render(
      <DropdownButton label="批量流转" align="start" items={<DropdownMenuItem>批量领用</DropdownMenuItem>} />,
    )
    openMenu('批量流转')
    expect((await screen.findByRole('menuitem', { name: '批量领用' })).closest('[data-align]'))
      .toHaveAttribute('data-align', 'start')
  })

  it('variant/size 透传给触发按钮（不显式传时走默认 outline/sm）', () => {
    render(<DropdownButton label="导入" items={<DropdownMenuItem>从 CSV 导入</DropdownMenuItem>} />)
    const btn = screen.getByRole('button', { name: '导入' })
    // sm 尺寸下箭头收窄为 size-3.5（与 outline 变体自带 border 类同时存在，只抽查关键类名）。
    expect(btn.className).toContain('h-8')
  })

  it('className 追加到触发按钮（如收窄 h-7 对齐同排紧凑按钮）', () => {
    render(<DropdownButton label="执行" className="h-7" items={<DropdownMenuItem>立即执行</DropdownMenuItem>} />)
    expect(screen.getByRole('button', { name: '执行' }).className).toContain('h-7')
  })
})
