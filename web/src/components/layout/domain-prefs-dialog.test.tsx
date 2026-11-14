import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { translateNavTitle, type NavT, type ResolvedDomain } from '@/lib/nav-utils'
import { DomainPrefsDialog } from './domain-prefs-dialog'
import { navDomains } from './nav-config'

const ts = ((key: string) => ({
  manageDomains: '管理业务域',
  restoreDomainDefaults: '恢复默认',
  undoRestoreDomainDefaults: '撤销恢复',
  cancel: '取消',
  save: '保存',
}[key] ?? key)) as NavT
const t = ((key: string) => key) as NavT

const domains: ResolvedDomain[] = navDomains.slice(0, 3).map((domain) => ({ domain, groups: [] }))
const [a, b, c] = domains.map((d) => d.domain)

function rowTitles() {
  return within(screen.getByRole('list')).getAllByRole('listitem').map((li) => li.textContent?.replace(/Alt\+\d/, '') ?? '')
}

function renderDialog(prefs = { order: [c!.key, b!.key, a!.key], hidden: [b!.key] }, onSave = vi.fn(() => true)) {
  render(<DomainPrefsDialog open onOpenChange={() => {}} domains={domains} prefs={prefs} onSave={onSave} t={t} ts={ts} />)
  return onSave
}

describe('DomainPrefsDialog 恢复默认', () => {
  it('恢复后可就地撤销，回到恢复前的顺序与开关', () => {
    const onSave = renderDialog()
    const before = rowTitles()

    fireEvent.click(screen.getByRole('button', { name: '恢复默认' }))
    expect(rowTitles()).toEqual([a!, b!, c!].map((d) => translateNavTitle(d, t)))

    fireEvent.click(screen.getByRole('button', { name: '撤销恢复' }))
    expect(rowTitles()).toEqual(before)

    fireEvent.click(screen.getByRole('button', { name: '保存' }))
    expect(onSave).toHaveBeenCalledWith({ order: [c!.key, b!.key, a!.key], hidden: [b!.key] })
  })

  it('恢复后再改动任何一项，撤销入口消失', () => {
    renderDialog()
    fireEvent.click(screen.getByRole('button', { name: '恢复默认' }))
    fireEvent.click(screen.getAllByRole('switch')[0]!)

    expect(screen.queryByRole('button', { name: '撤销恢复' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '恢复默认' })).toBeEnabled()
  })

  it('已是默认时「恢复默认」不可点', () => {
    renderDialog({ order: [], hidden: [] })
    expect(screen.getByRole('button', { name: '恢复默认' })).toBeDisabled()
  })
})
