import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { ExportPermissionReportButton } from './export-permission-report-button'

const { downloadXlsx, toast } = vi.hoisted(() => ({
  downloadXlsx: vi.fn(),
  toast: vi.fn(),
}))

vi.mock('@/lib/excel-io', () => ({ downloadXlsx }))
vi.mock('@/components/ui/use-toast', () => ({ toast }))

function render(dirty: boolean) {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <ExportPermissionReportButton dirty={dirty} />
    </NextIntlClientProvider>,
  )
}

beforeEach(() => {
  downloadXlsx.mockReset()
  toast.mockReset()
})

const permission = zhHansMessages.Account.permission

describe('ExportPermissionReportButton', () => {
  it('downloads the saved permission report', async () => {
    downloadXlsx.mockResolvedValue(undefined)
    render(false)
    fireEvent.click(screen.getByRole('button', { name: permission.exportButton }))
    expect(downloadXlsx).not.toHaveBeenCalled()
    expect(screen.getByText(permission.exportScopeHint)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: permission.exportConfirm }))
    await waitFor(() => {
      expect(downloadXlsx).toHaveBeenCalledWith(
        '/api/proxy/account-roles/permission-report',
        permission.exportFileName,
      )
    })
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({
      title: permission.exportSuccessTitle,
      description: permission.exportContentsHint,
    }))
  })

  it('mentions unsaved checks are left out of the file', async () => {
    downloadXlsx.mockResolvedValue(undefined)
    render(true)
    fireEvent.click(screen.getByRole('button', { name: permission.exportButton }))
    expect(screen.getByText(permission.exportUnsavedHint)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: permission.exportConfirm }))
    await waitFor(() => {
      expect(toast).toHaveBeenCalledWith(expect.objectContaining({
        description: permission.exportUnsavedHint,
      }))
    })
  })
})
