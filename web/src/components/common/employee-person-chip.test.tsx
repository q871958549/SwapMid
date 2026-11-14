import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { EmployeePersonChip, type EmployeePersonRef } from './employee-person-chip'

vi.mock('next-intl', () => ({
  useTranslations: () => (key: string, params?: Record<string, unknown>) =>
    params ? `${key} ${Object.values(params).join(' ')}` : key,
}))

vi.mock('@/components/ui/popover', () => ({
  Popover: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  PopoverTrigger: ({ children }: { children: ReactNode }) => <>{children}</>,
  PopoverContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}))

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  badgeVariants: () => '',
}))

const deng: EmployeePersonRef = {
  employeeNo: 'SM005353',
  name: '邓育亮',
  status: 2,
}

describe('EmployeePersonChip', () => {
  it('离职人员在花片正面标已离职', () => {
    render(<EmployeePersonChip person={deng} />)
    expect(screen.getByRole('button', { name: /邓育亮/ })).toHaveTextContent('employeeChip.statusResigned')
  })

  it('在职人员正面不标在职', () => {
    render(<EmployeePersonChip person={{ ...deng, status: 0, name: '肖发' }} />)
    const button = screen.getByRole('button', { name: /肖发/ })
    expect(button).not.toHaveTextContent('employeeChip.statusActive')
    expect(button).not.toHaveTextContent('employeeChip.statusResigned')
  })
})
