import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ExecutionsPageData } from './page-data'

const VERIFY_PERMISSION =
  'SwapMid.DataIntegration.PipelineExecutions.VerifyTargetReceipt'
const RESOLVE_PERMISSION =
  'SwapMid.DataIntegration.PipelineExecutions.ResolveOutcome'

const mocks = vi.hoisted(() => ({
  apiFetch: vi.fn(),
  getSession: vi.fn(),
  clientProps: vi.fn(),
}))

vi.mock('@/lib/api-client', () => ({
  apiFetch: mocks.apiFetch,
  rethrowApiFetchControlFlow: vi.fn(),
}))
vi.mock('@/lib/session', () => ({ getSession: mocks.getSession }))
vi.mock('./client', () => ({
  PipelineExecutionsClient: (props: {
    canResolveOutcome?: boolean
    canVerifyTargetReceipt?: boolean
  }) => {
    mocks.clientProps(props)
    return (
      <div
        data-testid="permission-props"
        data-resolve={String(props.canResolveOutcome)}
        data-verify={String(props.canVerifyTargetReceipt)}
      />
    )
  },
}))

beforeEach(() => {
  mocks.apiFetch.mockReset()
  mocks.getSession.mockReset()
  mocks.clientProps.mockReset()
  mocks.apiFetch.mockResolvedValue({ items: [], totalCount: 0 })
})

describe('ExecutionsPage · Target Receipt v1 权限隔离', () => {
  it('机器核验入口只读取独立 VerifyTargetReceipt 权限，不复用人工 ResolveOutcome', async () => {
    mocks.getSession.mockResolvedValue({
      grantedPolicies: {
        [VERIFY_PERMISSION]: true,
        [RESOLVE_PERMISSION]: false,
      },
    })

    render(await ExecutionsPageData({ searchParams: Promise.resolve({}) }))

    expect(screen.getByTestId('permission-props')).toHaveAttribute('data-verify', 'true')
    expect(screen.getByTestId('permission-props')).toHaveAttribute('data-resolve', 'false')
  })

  it('仅有人工 ResolveOutcome 权限时不得透出机器核验能力', async () => {
    mocks.getSession.mockResolvedValue({
      grantedPolicies: {
        [VERIFY_PERMISSION]: false,
        [RESOLVE_PERMISSION]: true,
      },
    })

    render(await ExecutionsPageData({ searchParams: Promise.resolve({}) }))

    expect(screen.getByTestId('permission-props')).toHaveAttribute('data-verify', 'false')
    expect(screen.getByTestId('permission-props')).toHaveAttribute('data-resolve', 'true')
  })
})
