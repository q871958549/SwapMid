import { assertSessionStoreConfiguration } from '@/lib/session-store-config'
import { assertProductionWebLeaseSupervision } from '@/lib/web-singleton-lease-core.mjs'
import { startWebSingletonParentWatchdog } from '@/lib/web-singleton-child-watchdog.mjs'

export async function registerNodeInstrumentation() {
  try {
    assertProductionWebLeaseSupervision()
    startWebSingletonParentWatchdog()
  } catch {
    console.error('[web-singleton] unsupervised production Next startup was rejected')
    process.exit(78)
  }

  assertSessionStoreConfiguration()

  if (process.env.NODE_ENV !== 'production' || process.env.ALLOW_INSECURE_TLS === 'true') {
    const { setGlobalDispatcher, Agent } = await import('undici')
    setGlobalDispatcher(new Agent({ connect: { rejectUnauthorized: false } }))
  }
}
