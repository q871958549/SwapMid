import {
  acquireSessionRefreshLease,
  settleSessionRefreshInvalidGrant,
  settleSessionRefreshSuccess,
  settleSessionRefreshUnavailable,
} from '../src/lib/session-refresh-protocol'
import { refreshSession } from '../src/lib/session-refresh'
import {
  deleteStoredSessionIfUnchanged,
  getStoredSession,
  getSessionStoreRedis,
  invalidateStoredSessionIfUnchanged,
  replaceIdentitySessionIfUnchanged,
  replaceStoredSessionIfUnchanged,
  sessionRevision,
} from '../src/lib/session-store'
import {
  startImpersonationForSessionId,
  stopImpersonationForSessionId,
  type ImpersonationTarget,
} from '../src/lib/session-identity-mutation'
import type { FullSession } from '../src/lib/session-types'

function required(name: string): string {
  const value = process.env[name]
  if (!value) throw new Error(`${name} is required`)
  return value
}

function parseSession(name: string): FullSession {
  return JSON.parse(required(name)) as FullSession
}

function emit(event: string, value: unknown): void {
  process.stdout.write(`${JSON.stringify({ event, value })}\n`)
}

function delay(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms))
}

async function waitForSettlePermission(): Promise<void> {
  const gateKey = process.env.SETTLE_GATE_KEY
  if (!gateKey) {
    await delay(Number(process.env.SETTLE_DELAY_MS ?? '0'))
    return
  }

  const redis = getSessionStoreRedis()
  if (!redis) throw new Error('SETTLE_GATE_KEY requires Redis mode')
  const deadline = Date.now() + 10_000
  while (Date.now() < deadline) {
    if (await redis.get(gateKey) === 'release') return
    await delay(25)
  }
  throw new Error(`settle gate timed out: ${gateKey}`)
}

async function main(): Promise<void> {
  const mode = required('WORKER_MODE')
  const sid = required('SESSION_ID')
  const session = parseSession('SESSION_JSON')

  if (mode === 'refresh') {
    emit('result', await refreshSession(sid, session))
    return
  }

  if (mode === 'identity-start') {
    const target = parseSession('UPDATED_SESSION_JSON') as ImpersonationTarget
    const current = await getStoredSession(sid)
    if (current.status !== 'found') {
      emit('identity-preflight', current.status)
      return
    }
    emit(
      'identity-preflight',
      await replaceIdentitySessionIfUnchanged(
        sid,
        sessionRevision(current.session),
        target,
      ),
    )
    emit(
      'result',
      await startImpersonationForSessionId(
        sid,
        target,
        required('EXPECTED_IDENTITY_VERSION'),
      ),
    )
    return
  }

  if (mode === 'identity-stop') {
    emit(
      'result',
      await stopImpersonationForSessionId(
        sid,
        required('EXPECTED_IDENTITY_VERSION'),
      ),
    )
    return
  }

  if (mode === 'replace') {
    emit(
      'result',
      await replaceStoredSessionIfUnchanged(
        sid,
        sessionRevision(session),
        parseSession('UPDATED_SESSION_JSON'),
      ),
    )
    return
  }

  if (mode === 'delete') {
    emit(
      'result',
      await deleteStoredSessionIfUnchanged(sid, sessionRevision(session)),
    )
    return
  }

  if (mode === 'invalidate') {
    emit(
      'result',
      await invalidateStoredSessionIfUnchanged(sid, sessionRevision(session)),
    )
    return
  }

  const acquisition = await acquireSessionRefreshLease(
    sid,
    sessionRevision(session),
    required('OWNER_ID'),
  )
  emit('acquired', acquisition)
  if (acquisition.status !== 'owner') return

  if (mode === 'oauth-gated-settle') {
    const response = await fetch(`${required('INTERNAL_API_URL')}/connect/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'refresh_token',
        client_id: 'SwapMid_App',
        refresh_token: session.refreshToken!,
      }),
    })
    if (!response.ok) throw new Error(`OAuth consume failed: ${response.status}`)
    const token = await response.json() as {
      access_token: string
      refresh_token: string
      expires_in: number
    }
    emit('oauth-consumed', true)
    await waitForSettlePermission()
    emit(
      'settled',
      await settleSessionRefreshSuccess(
        sid,
        sessionRevision(session),
        acquisition.lease,
        {
          ...session,
          accessToken: token.access_token,
          refreshToken: token.refresh_token,
          accessTokenExpiresAt: Date.now() + token.expires_in * 1_000,
        },
      ),
    )
    return
  }

  await waitForSettlePermission()
  const action = process.env.SETTLE_ACTION ?? 'success'
  if (action === 'invalid') {
    emit(
      'settled',
      await settleSessionRefreshInvalidGrant(
        sid,
        sessionRevision(session),
        acquisition.lease,
      ),
    )
    return
  }
  if (action === 'unavailable') {
    emit(
      'settled',
      await settleSessionRefreshUnavailable(
        sid,
        sessionRevision(session),
        acquisition.lease,
      ),
    )
    return
  }
  emit(
    'settled',
    await settleSessionRefreshSuccess(
      sid,
      sessionRevision(session),
      acquisition.lease,
      parseSession('UPDATED_SESSION_JSON'),
    ),
  )
}

void main()
  .catch((error: unknown) => {
    console.error(error)
    process.exitCode = 1
  })
  .finally(() => {
    getSessionStoreRedis()?.disconnect()
  })
