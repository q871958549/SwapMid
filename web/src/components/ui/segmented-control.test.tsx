import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { SegmentedControl } from './segmented-control'
import { FilterChip } from './filter-chip'

function Segmented({ onChange = () => {} }: { onChange?: (value: string) => void }) {
  const [value, setValue] = useState('all')
  return (
    <SegmentedControl
      aria-label="状态"
      value={value}
      onValueChange={(next) => { setValue(next); onChange(next) }}
      options={[
        { value: 'all', label: '全部' },
        { value: 'pending', label: '待补档', count: 12 },
        { value: 'locked', label: '已锁定', disabled: true },
        { value: 'done', label: '已完成' },
      ]}
    />
  )
}

describe('SegmentedControl', () => {
  it('单选语义：radiogroup + radio，选中项可 Tab 聚焦，其余不进 Tab 顺序', () => {
    render(<Segmented />)
    expect(screen.getByRole('radiogroup', { name: '状态' })).toBeInTheDocument()
    const all = screen.getByRole('radio', { name: '全部' })
    expect(all).toHaveAttribute('aria-checked', 'true')
    expect(all).toHaveAttribute('tabindex', '0')
    expect(screen.getByRole('radio', { name: /待补档/ })).toHaveAttribute('tabindex', '-1')
  })

  it('选中项用白滑块，不用黑底主按钮色', () => {
    render(<Segmented />)
    const all = screen.getByRole('radio', { name: '全部' })
    expect(all).toHaveClass('bg-background')
    expect(all.className).not.toContain('bg-primary')
  })

  it('方向键切换并立即生效，跳过禁用项、首尾循环', () => {
    const onChange = vi.fn()
    render(<Segmented onChange={onChange} />)
    fireEvent.keyDown(screen.getByRole('radio', { name: '全部' }), { key: 'ArrowRight' })
    expect(onChange).toHaveBeenLastCalledWith('pending')
    fireEvent.keyDown(screen.getByRole('radio', { name: /待补档/ }), { key: 'ArrowRight' })
    expect(onChange).toHaveBeenLastCalledWith('done')
    fireEvent.keyDown(screen.getByRole('radio', { name: '已完成' }), { key: 'ArrowRight' })
    expect(onChange).toHaveBeenLastCalledWith('all')
    fireEvent.keyDown(screen.getByRole('radio', { name: '全部' }), { key: 'End' })
    expect(onChange).toHaveBeenLastCalledWith('done')
  })

  it('作表单字段：一项未选时第一个可用项进 Tab 顺序；id 与校验失败态落在 radiogroup 上', () => {
    render(
      <SegmentedControl
        id="gender"
        aria-label="性别"
        aria-invalid
        aria-describedby="gender-error"
        value=""
        onValueChange={() => {}}
        options={[
          { value: '0', label: '男', disabled: true },
          { value: '1', label: '女' },
        ]}
      />,
    )
    const group = screen.getByRole('radiogroup', { name: '性别' })
    expect(group).toHaveAttribute('id', 'gender')
    expect(group).toHaveAttribute('aria-invalid', 'true')
    expect(group).toHaveAttribute('aria-describedby', 'gender-error')
    expect(screen.getByRole('radio', { name: '男' })).toHaveAttribute('tabindex', '-1')
    expect(screen.getByRole('radio', { name: '女' })).toHaveAttribute('tabindex', '0')
  })
})

describe('FilterChip', () => {
  it('按下后 aria-pressed、浅灰底并出现对勾；再点取消', () => {
    function Chip() {
      const [on, setOn] = useState(false)
      return <FilterChip pressed={on} onPressedChange={setOn} count={3}>超期</FilterChip>
    }
    render(<Chip />)
    const chip = screen.getByRole('button', { name: /超期/ })
    expect(chip).toHaveAttribute('aria-pressed', 'false')
    expect(chip.querySelector('svg')).toBeNull()
    fireEvent.click(chip)
    expect(chip).toHaveAttribute('aria-pressed', 'true')
    expect(chip.querySelector('svg')).not.toBeNull()
    expect(chip.className).toContain('aria-pressed:bg-secondary')
    fireEvent.click(chip)
    expect(chip).toHaveAttribute('aria-pressed', 'false')
  })
})
