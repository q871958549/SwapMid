import {
  WEB_LEASE_HEARTBEAT_PROTOCOL,
  WEB_LEASE_HEARTBEAT_TYPE,
  WEB_LEASE_SUPERVISED_ENV,
  WEB_LEASE_WATCHDOG_ENV,
} from './web-singleton-lease-core.mjs'

function isHeartbeat(message) {
  return typeof message === 'object'
    && message !== null
    && message.type === WEB_LEASE_HEARTBEAT_TYPE
    && message.protocol === WEB_LEASE_HEARTBEAT_PROTOCOL
}

/**
 * @param {{
 *   environment?: Record<string, string | undefined>,
 *   processLike?: {
 *     connected?: boolean,
 *     on: (event: string, listener: (...args: unknown[]) => void) => void,
 *     removeListener: (
 *       event: string,
 *       listener: (...args: unknown[]) => void,
 *     ) => void,
 *   },
 *   setTimer?: typeof setTimeout,
 *   clearTimer?: typeof clearTimeout,
 *   terminate?: (exitCode: number) => void,
 *   logError?: (message: string) => void,
 * }} [options]
 */
export function startWebSingletonParentWatchdog({
  environment = process.env,
  processLike = process,
  setTimer = setTimeout,
  clearTimer = clearTimeout,
  terminate = exitCode => process.exit(exitCode),
  logError = message => console.error(message),
} = {}) {
  if (environment.NODE_ENV !== 'production') {
    return { dispose() {} }
  }
  if (environment[WEB_LEASE_SUPERVISED_ENV] !== '1') {
    throw new Error('Production web child is missing lease supervision')
  }
  if (!processLike.connected) {
    throw new Error('Production web child has no parent IPC channel')
  }

  const rawTimeout = environment[WEB_LEASE_WATCHDOG_ENV]
  if (!rawTimeout || !/^[1-9]\d*$/.test(rawTimeout)) {
    throw new Error('Production web child watchdog timeout is invalid')
  }
  const timeoutMs = Number(rawTimeout)
  if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 1_000 || timeoutMs > 60_000) {
    throw new Error('Production web child watchdog timeout is invalid')
  }

  let timer
  let stopped = false
  const terminateOnce = (message) => {
    if (stopped) return
    stopped = true
    if (timer) clearTimer(timer)
    logError(message)
    terminate(79)
  }
  const arm = () => {
    if (stopped) return
    if (timer) clearTimer(timer)
    timer = setTimer(() => {
      terminateOnce(
        '[web-singleton] parent lease heartbeat expired; terminating Next',
      )
    }, timeoutMs)
    timer?.unref?.()
  }
  const onMessage = (message) => {
    if (isHeartbeat(message)) arm()
  }
  const onDisconnect = () => {
    terminateOnce(
      '[web-singleton] lease parent disconnected; terminating Next',
    )
  }

  processLike.on('message', onMessage)
  processLike.on('disconnect', onDisconnect)
  arm()

  return {
    dispose() {
      if (stopped) return
      stopped = true
      if (timer) clearTimer(timer)
      processLike.removeListener('message', onMessage)
      processLike.removeListener('disconnect', onDisconnect)
    },
  }
}
