import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Dialog, DialogContent, DialogFooter, DialogTitle, pinsDialogFooter } from './dialog'

function open(className?: string) {
  render(
    <Dialog open>
      <DialogContent className={className} aria-describedby={undefined}>
        <DialogTitle>标题</DialogTitle>
        <DialogFooter>保存</DialogFooter>
      </DialogContent>
    </Dialog>,
  )
  return screen.getByRole('dialog')
}

describe('pinsDialogFooter', () => {
  it('默认内边距、面板整体滚动：页脚钉底', () => {
    expect(pinsDialogFooter()).toBe(true)
    expect(pinsDialogFooter('sm:max-w-3xl max-h-[85vh]')).toBe(true)
  })

  it('调用方改了内边距：不钉，避免负边距错位', () => {
    expect(pinsDialogFooter('p-0 sm:max-w-2xl')).toBe(false)
    expect(pinsDialogFooter('sm:max-w-lg md:px-8')).toBe(false)
  })

  it('调用方自己分头 / 体 / 脚（overflow-hidden）：不钉', () => {
    expect(pinsDialogFooter('flex max-h-[88vh] flex-col overflow-hidden')).toBe(false)
  })

  it('pointer-events、max-sm:top-0 一类不是内边距', () => {
    expect(pinsDialogFooter('pointer-events-auto max-sm:top-0')).toBe(true)
  })
})

describe('DialogContent', () => {
  it('默认面板带 data-pin-footer，页脚带钉底类', () => {
    const dialog = open()
    expect(dialog).toHaveAttribute('data-pin-footer')
    expect(screen.getByText('保存').className).toContain('group-data-[pin-footer]/dialog:sticky')
  })

  it('自定义内边距的面板不带 data-pin-footer', () => {
    expect(open('p-0')).not.toHaveAttribute('data-pin-footer')
  })
})
