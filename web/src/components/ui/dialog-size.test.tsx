import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { Dialog, DialogContent, DialogTitle, dialogSizeClasses } from './dialog'
import { Sheet, SheetContent, SheetTitle, sheetSizeClasses } from './sheet'

describe('DialogContent 与 SheetContent 尺寸档位', () => {
  it('DialogContent 默认使用 md 档（sm:max-w-lg），指定 size 时切换为对应标准档位且支持 className 覆盖', () => {
    const { rerender } = render(
      <Dialog open>
        <DialogContent aria-describedby={undefined}>
          <DialogTitle>默认弹窗</DialogTitle>
        </DialogContent>
      </Dialog>,
    )

    let content = document.querySelector('[data-slot="dialog-content"]')
    expect(content).toHaveAttribute('data-size', 'md')
    expect(content?.className).toContain(dialogSizeClasses.md)

    rerender(
      <Dialog open>
        <DialogContent size="xl" aria-describedby={undefined}>
          <DialogTitle>宽弹窗</DialogTitle>
        </DialogContent>
      </Dialog>,
    )
    content = document.querySelector('[data-slot="dialog-content"]')
    expect(content).toHaveAttribute('data-size', 'xl')
    expect(content?.className).toContain(dialogSizeClasses.xl)
    expect(content?.className).not.toContain(dialogSizeClasses.md)

    rerender(
      <Dialog open>
        <DialogContent className="sm:max-w-3xl" aria-describedby={undefined}>
          <DialogTitle>自定义覆盖</DialogTitle>
        </DialogContent>
      </Dialog>,
    )
    content = document.querySelector('[data-slot="dialog-content"]')
    expect(content?.className).toContain('sm:max-w-3xl')
    expect(content?.className).not.toContain('sm:max-w-lg')
  })

  it('SheetContent 左右抽屉按 size 档位切换最大宽度，上下抽屉不附加横向 max-w', () => {
    const { rerender } = render(
      <Sheet open>
        <SheetContent size="lg" aria-describedby={undefined}>
          <SheetTitle>设置抽屉</SheetTitle>
        </SheetContent>
      </Sheet>,
    )

    let content = document.querySelector('[data-slot="sheet-content"]')
    expect(content).toHaveAttribute('data-size', 'lg')
    expect(content?.className).toContain(sheetSizeClasses.lg)

    rerender(
      <Sheet open>
        <SheetContent side="bottom" size="lg" aria-describedby={undefined}>
          <SheetTitle>底部抽屉</SheetTitle>
        </SheetContent>
      </Sheet>,
    )
    content = document.querySelector('[data-slot="sheet-content"]')
    expect(content).not.toHaveAttribute('data-size')
    expect(content?.className).not.toContain(sheetSizeClasses.lg)
  })
})
