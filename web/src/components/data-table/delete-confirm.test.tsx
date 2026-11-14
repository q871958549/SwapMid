import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { DeleteConfirm } from './delete-confirm'
import zhHansMessages from '../../../messages/zh-Hans.json'

// DeleteConfirm（及其内部的 ConfirmDialog）经 useTranslations('Common') 求默认标题/描述/按钮文案
// （i18n 第二期批次2b）——组件树需要 NextIntlClientProvider 才能渲染。真实 zh-Hans messages 直供。
function renderWithIntl(ui: React.ReactElement) {
  return render(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

interface Row { id: string; name: string }
const row: Row = { id: '1', name: '测试项' }

function makeCrud(overrides: Partial<{ deleteTarget: Row | null; deleting: boolean }> = {}) {
  return {
    deleteTarget: row as Row | null,
    closeDelete: vi.fn(),
    confirmDelete: vi.fn(),
    deleting: false,
    ...overrides,
  }
}

describe('DeleteConfirm', () => {
  it('deleteTarget 非空时打开,默认标题 = 确认删除{entityName}', () => {
    renderWithIntl(<DeleteConfirm crud={makeCrud()} entityName="分组" />)
    expect(screen.getByText('确认删除分组')).toBeInTheDocument()
  })

  it('deleteTarget 为 null 时不渲染对话框内容', () => {
    renderWithIntl(<DeleteConfirm crud={makeCrud({ deleteTarget: null })} entityName="分组" />)
    expect(screen.queryByText('确认删除分组')).not.toBeInTheDocument()
  })

  it('title 覆盖默认标题', () => {
    renderWithIntl(<DeleteConfirm crud={makeCrud()} entityName="分组" title="确认删除角色" />)
    expect(screen.getByText('确认删除角色')).toBeInTheDocument()
    expect(screen.queryByText('确认删除分组')).not.toBeInTheDocument()
  })

  it('description 函数收到 target 并渲染其返回值', () => {
    const description = vi.fn((t: Row) => `删除「${t.name}」`)
    renderWithIntl(<DeleteConfirm crud={makeCrud()} entityName="分组" description={description} />)
    expect(description).toHaveBeenCalledWith(row)
    expect(screen.getByText('删除「测试项」')).toBeInTheDocument()
  })

  it('未传 description 时用含 entityName 的默认文案', () => {
    renderWithIntl(<DeleteConfirm crud={makeCrud()} entityName="房间" />)
    expect(screen.getByText(/删除该房间后无法恢复/)).toBeInTheDocument()
  })

  it('确认按钮默认文案「删除」,点击调用 crud.confirmDelete', () => {
    const crud = makeCrud()
    renderWithIntl(<DeleteConfirm crud={crud} entityName="分组" />)
    fireEvent.click(screen.getByRole('button', { name: '删除' }))
    expect(crud.confirmDelete).toHaveBeenCalledTimes(1)
  })

  it('confirmText 覆盖确认按钮文案', () => {
    renderWithIntl(<DeleteConfirm crud={makeCrud()} entityName="分组" confirmText="永久删除" />)
    expect(screen.getByRole('button', { name: '永久删除' })).toBeInTheDocument()
  })

  it('deleting=true 时确认按钮禁用并显示「删除中...」', () => {
    renderWithIntl(<DeleteConfirm crud={makeCrud({ deleting: true })} entityName="分组" />)
    const btn = screen.getByRole('button', { name: '删除中...' })
    expect(btn).toBeDisabled()
  })
})
