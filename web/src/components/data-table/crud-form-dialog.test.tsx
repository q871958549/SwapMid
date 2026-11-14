import { describe, it, expect, vi } from 'vitest'
import { render as rtlRender, screen, fireEvent, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { CrudFormDialog } from './crud-form-dialog'
import { Button } from '@/components/ui/button'
import { type CrudFormController } from '@/lib/crud/use-crud-form'
import zhHansMessages from '../../../messages/zh-Hans.json'

// CrudFormDialog 内部经 useTranslations('Common') 求默认按钮文案（保存/保存中.../取消，
// i18n 第二期批次2b）——组件树需要 NextIntlClientProvider 才能渲染。真实 zh-Hans messages 直供。
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

const title = { create: '新建岗位', edit: '编辑岗位' }

function makeController(overrides: Partial<CrudFormController> = {}): CrudFormController {
  return {
    mode: 'create',
    isOpen: true,
    submitting: false,
    formError: null,
    close: vi.fn(),
    submit: vi.fn().mockResolvedValue(undefined),
    isDirty: () => false,
    ...overrides,
  }
}

describe('CrudFormDialog', () => {
  it('按 mode 切换标题：create → title.create', () => {
    render(
      <CrudFormDialog crud={makeController({ mode: 'create' })} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.getByText('新建岗位')).toBeInTheDocument()
    expect(screen.queryByText('编辑岗位')).not.toBeInTheDocument()
    expect(screen.getByText('字段区')).toBeInTheDocument()
  })

  it('按 mode 切换标题：edit → title.edit', () => {
    render(
      <CrudFormDialog crud={makeController({ mode: 'edit' })} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.getByText('编辑岗位')).toBeInTheDocument()
  })

  it('isOpen=false 时不渲染内容', () => {
    render(
      <CrudFormDialog crud={makeController({ isOpen: false })} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.queryByText('字段区')).not.toBeInTheDocument()
    expect(screen.queryByText('新建岗位')).not.toBeInTheDocument()
  })

  it('canSubmit=false 时禁用「保存」', () => {
    render(
      <CrudFormDialog crud={makeController()} title={title} canSubmit={false}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.getByRole('button', { name: '保存' })).toBeDisabled()
  })

  it('canSubmit 未传（undefined）时「保存」可点', () => {
    render(
      <CrudFormDialog crud={makeController()} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.getByRole('button', { name: '保存' })).toBeEnabled()
  })

  it('submitting=true 时禁用并显示 submittingLabel', () => {
    render(
      <CrudFormDialog crud={makeController({ submitting: true })} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    const btn = screen.getByRole('button', { name: '保存中...' })
    expect(btn).toBeDisabled()
  })

  it('formError 非空时渲染内联错误', () => {
    render(
      <CrudFormDialog crud={makeController({ formError: '岗位编码已存在' })} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.getByText('岗位编码已存在')).toBeInTheDocument()
  })

  it('点「保存」调用 crud.submit', () => {
    const crud = makeController()
    render(
      <CrudFormDialog crud={crud} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    fireEvent.click(screen.getByRole('button', { name: '保存' }))
    expect(crud.submit).toHaveBeenCalledTimes(1)
  })

  it('点「取消」调用 crud.close', () => {
    const crud = makeController()
    render(
      <CrudFormDialog crud={crud} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    expect(crud.close).toHaveBeenCalledTimes(1)
  })

  // —— 防误关（脏检查拦截）——
  // 拦的是 Radix DismissableLayer 的两条「非精确」关闭路径（点遮罩 / Esc）；「取消」与 X 不拦。

  it('无改动时按 Esc 正常关闭', () => {
    const crud = makeController({ isDirty: () => false })
    render(
      <CrudFormDialog crud={crud} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(crud.close).toHaveBeenCalledTimes(1)
  })

  it('有改动时按 Esc 被拦截，不关闭并提示未保存', () => {
    const crud = makeController({ isDirty: () => true })
    render(
      <CrudFormDialog crud={crud} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(crud.close).not.toHaveBeenCalled()
    expect(screen.getByRole('status')).toHaveTextContent('有未保存的修改')
  })

  it('有改动时「取消」按钮仍能直接关闭（精确点击目标不拦）', () => {
    const crud = makeController({ isDirty: () => true })
    render(
      <CrudFormDialog crud={crud} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    fireEvent.click(screen.getByRole('button', { name: '取消' }))
    expect(crud.close).toHaveBeenCalledTimes(1)
  })

  // —— 三段式布局：header 固定 / 字段区内滚 / 提示+footer 固定 ——
  // 守的是「拦截提示与提交错误必须在滚动区之外」这条不变量：它们若回到字段流内，长表单下会被
  // 推出视口，用户点遮罩看不到任何反馈——正是本次改动要根治的「有时候点外面关不掉」。

  function fieldsArea() {
    return document.querySelector('[data-slot="crud-form-fields"]') as HTMLElement
  }

  it('children 渲染在字段滚动区内', () => {
    render(
      <CrudFormDialog crud={makeController()} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(within(fieldsArea()).getByText('字段区')).toBeInTheDocument()
  })

  it('拦截提示渲染在字段滚动区之外', () => {
    const crud = makeController({ isDirty: () => true })
    render(
      <CrudFormDialog crud={crud} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    const status = screen.getByRole('status')
    expect(status).toHaveTextContent('有未保存的修改')
    expect(fieldsArea().contains(status)).toBe(false)
  })

  it('formError 渲染在字段滚动区之外', () => {
    render(
      <CrudFormDialog crud={makeController({ formError: '岗位编码已存在' })} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    const err = screen.getByText('岗位编码已存在')
    expect(fieldsArea().contains(err)).toBe(false)
  })

  it('滚动下沉到字段区：外层关闭纵向滚动，字段区自己滚', () => {
    render(
      <CrudFormDialog crud={makeController()} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    // overflow-y-hidden 与基类 overflow-y-auto 同属 twMerge 的 overflow-y 组，必被覆盖；
    // 若有人改成 overflow-hidden（不同组、不覆盖），基类的 overflow-y-auto 会残留、本断言失败。
    const content = document.querySelector('[data-slot="dialog-content"]') as HTMLElement
    expect(content.className).toContain('overflow-y-hidden')
    expect(content.className).not.toContain('overflow-y-auto')
    expect(fieldsArea().className).toContain('overflow-y-auto')
    expect(fieldsArea().className).toContain('min-h-0')
    expect(fieldsArea().className).toContain('flex-1')
  })

  it('无提示也无错误时不渲染提示容器（矮弹窗外观不变）', () => {
    render(
      <CrudFormDialog crud={makeController()} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(document.querySelector('[data-slot="crud-form-notices"]')).toBeNull()
  })

  it('支持自定义 submitLabel', () => {
    render(
      <CrudFormDialog crud={makeController()} title={title} submitLabel="提交" submittingLabel="提交中...">
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.getByRole('button', { name: '提交' })).toBeInTheDocument()
  })

  it('传入 footerExtra 时渲染到 footer，且保留默认取消/保存', () => {
    render(
      <CrudFormDialog
        crud={makeController()}
        title={title}
        footerExtra={<button type="button">测试连接</button>}
      >
        <div>字段区</div>
      </CrudFormDialog>,
    )
    expect(screen.getByRole('button', { name: '测试连接' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '保存' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '取消' })).toBeInTheDocument()
  })

  it('按 Esc 关闭对话框时调用 crud.close（关闭清错路径）', () => {
    const crud = makeController()
    render(
      <CrudFormDialog crud={crud} title={title}>
        <div>字段区</div>
      </CrudFormDialog>,
    )
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape', code: 'Escape' })
    expect(crud.close).toHaveBeenCalled()
  })

  describe('submitExtra（并排次要提交按钮）', () => {
    it('不传时只有「取消」「保存」，无第三个按钮（现状零影响）', () => {
      render(
        <CrudFormDialog crud={makeController()} title={title}>
          <div>字段区</div>
        </CrudFormDialog>,
      )
      // 范围限定在 footer 容器内：DialogContent 自带右上角 Close(X) 按钮（可访问名 "Close"，
      // Radix 内建、与 footer 无关），若不限定范围会被 getAllByRole 一并计入干扰按钮计数。
      const footer = document.querySelector('[data-slot="dialog-footer"]') as HTMLElement
      expect(within(footer).getByRole('button', { name: '取消' })).toBeInTheDocument()
      expect(within(footer).getByRole('button', { name: '保存' })).toBeInTheDocument()
      expect(within(footer).getAllByRole('button')).toHaveLength(2)
    })

    it('传入 submitExtra 时 footer 同时渲染「保存」和「保存并打印标签」；点击调用其自身 onClick（本壳不拦截）', () => {
      const onSaveAndPrint = vi.fn()
      render(
        <CrudFormDialog
          crud={makeController()}
          title={title}
          submitExtra={<Button variant="secondary" onClick={onSaveAndPrint}>保存并打印标签</Button>}
        >
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.getByRole('button', { name: '保存' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: '保存并打印标签' })).toBeInTheDocument()
      fireEvent.click(screen.getByRole('button', { name: '保存并打印标签' }))
      expect(onSaveAndPrint).toHaveBeenCalledTimes(1)
    })

    it('点「保存」仍调用 crud.submit（主按钮不受 submitExtra 影响）', () => {
      const crud = makeController()
      render(
        <CrudFormDialog
          crud={crud}
          title={title}
          submitExtra={<Button variant="secondary">保存并打印标签</Button>}
        >
          <div>字段区</div>
        </CrudFormDialog>,
      )
      fireEvent.click(screen.getByRole('button', { name: '保存' }))
      expect(crud.submit).toHaveBeenCalledTimes(1)
    })

    it('submitExtra 自身的 disabled 由调用方控制，不受 canSubmit 影响', () => {
      render(
        <CrudFormDialog
          crud={makeController()}
          title={title}
          canSubmit={true}
          submitExtra={<Button variant="secondary" disabled>保存并打印标签</Button>}
        >
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.getByRole('button', { name: '保存' })).toBeEnabled()
      expect(screen.getByRole('button', { name: '保存并打印标签' })).toBeDisabled()
    })

    it('canSubmit=false 只禁用主「保存」，不波及 submitExtra 自身未禁用的按钮（两者独立）', () => {
      render(
        <CrudFormDialog
          crud={makeController()}
          title={title}
          canSubmit={false}
          submitExtra={<Button variant="secondary">保存并打印标签</Button>}
        >
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.getByRole('button', { name: '保存' })).toBeDisabled()
      expect(screen.getByRole('button', { name: '保存并打印标签' })).toBeEnabled()
    })
  })

  // 禁用按钮是个无声信号：用户看到灰按钮却不知道差哪一项（2026-08-11 用户反馈）。
  // 这批用例同时守住「存量 53 个调用方不传该 prop 时行为分毫不变」。
  describe('missingFields（缺失必填项提示）', () => {
    it('非空时渲染「还需填写：…」并列出全部缺失项', () => {
      render(
        <CrudFormDialog crud={makeController()} title={title} canSubmit={false}
          missingFields={['选择印章', '选择文件']}>
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.getByText('还需填写：选择印章、选择文件')).toBeInTheDocument()
    })

    it('空数组不渲染任何提示', () => {
      render(
        <CrudFormDialog crud={makeController()} title={title} missingFields={[]}>
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.queryByText(/还需填写/)).not.toBeInTheDocument()
    })

    it('不传该 prop 时零渲染——存量调用方行为不变', () => {
      render(
        <CrudFormDialog crud={makeController()} title={title} canSubmit={false}>
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.queryByText(/还需填写/)).not.toBeInTheDocument()
      expect(document.querySelector('[data-slot="crud-form-notices"]')).toBeNull()
    })

    it('提交中不提示——此刻表单必然已齐，留着只会和「保存中…」互相矛盾', () => {
      render(
        <CrudFormDialog crud={makeController({ submitting: true })} title={title}
          missingFields={['选择文件']}>
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.queryByText(/还需填写/)).not.toBeInTheDocument()
    })

    it('提示渲染在 notices 区而非字段滚动区——落进滚动区就会随字段滚走、等于没有反馈', () => {
      render(
        <CrudFormDialog crud={makeController()} title={title} missingFields={['选择文件']}>
          <div>字段区</div>
        </CrudFormDialog>,
      )
      const notices = document.querySelector('[data-slot="crud-form-notices"]')
      const fields = document.querySelector('[data-slot="crud-form-fields"]')
      expect(within(notices as HTMLElement).getByText(/还需填写/)).toBeInTheDocument()
      expect(within(fields as HTMLElement).queryByText(/还需填写/)).toBeNull()
    })

    it('与提交错误共存：缺项提示与 formError 同时可见，两者语义不同不互相顶替', () => {
      render(
        <CrudFormDialog crud={makeController({ formError: '服务端拒绝了该编码' })} title={title}
          missingFields={['选择文件']}>
          <div>字段区</div>
        </CrudFormDialog>,
      )
      expect(screen.getByText('还需填写：选择文件')).toBeInTheDocument()
      expect(screen.getByText('服务端拒绝了该编码')).toBeInTheDocument()
    })
  })
})
