import {
  WEB_LEASE_HEARTBEAT_PROTOCOL,
  WEB_LEASE_HEARTBEAT_TYPE,
  WebSingletonLeaseAlreadyHeldError,
  WebSingletonLeaseBackendError,
} from './web-singleton-lease-core.mjs'

function noOp() {}

/**
 * Owns the Redis lease for the entire child lifetime. A new owner first waits
 * through a renewed quarantine window so an old owner has time to observe loss
 * and for its child watchdog to terminate any orphan.
 */
export class WebSingletonLeaseSupervisor {
  /**
   * @param {{
   *   lease: {
   *     acquire: () => Promise<boolean>,
   *     renew: () => Promise<boolean>,
   *     release: () => Promise<boolean>,
   *   },
   *   spawnChild: () => {
   *     connected?: boolean,
   *     once: (event: string, listener: (...args: unknown[]) => void) => void,
   *     kill: (signal: string) => boolean,
   *     send: (
   *       message: unknown,
   *       callback?: (error: Error | null) => void,
   *     ) => boolean,
   *   },
   *   renewIntervalMs: number,
   *   takeoverIsolationMs: number,
   *   heartbeatIntervalMs: number,
   *   heartbeatFreshnessMs: number,
   *   shutdownGraceMs: number,
   *   failureGraceMs: number,
   *   forceKillWaitMs: number,
   *   now?: () => number,
   *   setTimer?: typeof setTimeout,
   *   clearTimer?: typeof clearTimeout,
   *   logError?: (message: string) => void,
   * }} options
   */
  constructor({
    lease,
    spawnChild,
    renewIntervalMs,
    takeoverIsolationMs,
    heartbeatIntervalMs,
    heartbeatFreshnessMs,
    shutdownGraceMs,
    failureGraceMs,
    forceKillWaitMs,
    now = () => performance.now(),
    setTimer = setTimeout,
    clearTimer = clearTimeout,
    logError = noOp,
  }) {
    this.lease = lease
    this.spawnChild = spawnChild
    this.renewIntervalMs = renewIntervalMs
    this.takeoverIsolationMs = takeoverIsolationMs
    this.heartbeatIntervalMs = heartbeatIntervalMs
    this.heartbeatFreshnessMs = heartbeatFreshnessMs
    this.shutdownGraceMs = shutdownGraceMs
    this.failureGraceMs = failureGraceMs
    this.forceKillWaitMs = forceKillWaitMs
    this.now = now
    this.setTimer = setTimer
    this.clearTimer = clearTimer
    this.logError = logError

    this.state = 'idle'
    this.acquired = false
    this.lastLeaseConfirmationAt = undefined
    this.child = undefined
    this.childClosed = false
    this.stopRequest = undefined
    this.renewTimer = undefined
    this.heartbeatTimer = undefined
    this.isolationTimer = undefined
    this.isolationWake = undefined
    this.forceTimer = undefined
    this.hardExitTimer = undefined
    this.renewInFlight = undefined
    this.completed = false
    this.completion = new Promise((resolve) => {
      this.resolveCompletion = resolve
    })
  }

  async start() {
    if (this.state !== 'idle') {
      throw new Error('Web singleton lease supervisor can only be started once')
    }

    this.state = 'acquiring'
    let acquired
    try {
      acquired = await this.lease.acquire()
    } catch {
      this.state = 'stopped'
      throw new WebSingletonLeaseBackendError()
    }

    if (!acquired) {
      this.state = 'stopped'
      throw new WebSingletonLeaseAlreadyHeldError()
    }

    this.acquired = true
    this.lastLeaseConfirmationAt = this.now()
    if (this.stopRequest) {
      await this.finishWithoutChild()
      return this
    }

    this.state = 'isolating'
    try {
      await this.waitForTakeoverIsolation()
    } catch {
      await this.releaseAfterChildExit()
      this.state = 'stopped'
      throw new WebSingletonLeaseBackendError()
    }

    if (this.stopRequest) {
      await this.finishWithoutChild()
      return this
    }

    await this.spawnAndMonitorChild()
    return this
  }

  async waitForTakeoverIsolation() {
    const deadline = this.now() + this.takeoverIsolationMs
    while (!this.stopRequest && this.now() < deadline) {
      const remaining = deadline - this.now()
      await this.waitForIsolationStep(
        Math.min(this.renewIntervalMs, remaining),
      )
      if (this.stopRequest) return
      await this.confirmRenewal()
    }
  }

  waitForIsolationStep(delayMs) {
    return new Promise((resolve) => {
      const finish = () => {
        if (this.isolationTimer) {
          this.clearTimer(this.isolationTimer)
          this.isolationTimer = undefined
        }
        this.isolationWake = undefined
        resolve()
      }
      this.isolationWake = finish
      this.isolationTimer = this.setTimer(finish, delayMs)
      this.isolationTimer?.unref?.()
    })
  }

  async spawnAndMonitorChild() {
    try {
      this.child = this.spawnChild()
    } catch {
      const released = await this.releaseAfterChildExit()
      this.state = 'stopped'
      if (!released) {
        this.logError(
          '[web-singleton] lease release after child startup failure was not confirmed',
        )
      }
      throw new Error('The production web server child could not be started')
    }

    this.state = 'running'
    this.child.once('error', () => {
      this.logError(
        '[web-singleton] web server child process failed; stopping',
      )
      void this.requestShutdown({ fatal: true })
    })
    this.child.once('disconnect', () => {
      if (!this.stopRequest && !this.childClosed) {
        this.logError(
          '[web-singleton] child IPC disconnected unexpectedly; stopping',
        )
        void this.requestShutdown({ fatal: true })
      }
    })
    this.child.once('close', () => {
      this.childClosed = true
      void this.finishAfterChildClose()
    })
    this.scheduleRenewal()
    this.sendHeartbeat()
  }

  requestShutdown({
    fatal = false,
    signal = 'SIGTERM',
  } = {}) {
    if (this.completed) return this.completion

    const hadStopRequest = this.stopRequest !== undefined
    const wasFatal = this.stopRequest?.fatal === true
    this.stopRequest = {
      fatal: wasFatal || fatal,
      signal: this.stopRequest?.signal ?? signal,
    }

    if (this.state === 'isolating') {
      this.isolationWake?.()
    }

    if (this.state === 'running' || this.state === 'stopping') {
      this.state = 'stopping'
      if (this.stopRequest.fatal) {
        this.clearRenewTimer()
        this.clearHeartbeatTimer()
      }
      const firstStopRequest = !hadStopRequest
      const upgradedToFatal = !wasFatal && this.stopRequest.fatal
      if (firstStopRequest || upgradedToFatal) {
        this.signalChild(this.stopRequest.signal)
        this.armForceKill(
          this.stopRequest.fatal
            ? this.failureGraceMs
            : this.shutdownGraceMs,
        )
      }
    }

    return this.completion
  }

  scheduleRenewal() {
    if (this.completed || this.childClosed || this.stopRequest?.fatal) return
    this.renewTimer = this.setTimer(() => {
      this.renewTimer = undefined
      void this.renewOnce()
    }, this.renewIntervalMs)
    this.renewTimer?.unref?.()
  }

  async confirmRenewal() {
    const renewal = this.lease.renew()
    this.renewInFlight = renewal
    try {
      if (!await renewal) throw new WebSingletonLeaseBackendError()
      this.lastLeaseConfirmationAt = this.now()
    } finally {
      if (this.renewInFlight === renewal) this.renewInFlight = undefined
    }
  }

  async renewOnce() {
    if (this.completed || this.childClosed || this.stopRequest?.fatal) return
    try {
      await this.confirmRenewal()
    } catch {
      this.logError(
        '[web-singleton] lease renewal is not confirmed; stopping the web server',
      )
      void this.requestShutdown({ fatal: true })
      return
    }
    this.scheduleRenewal()
  }

  sendHeartbeat() {
    if (this.completed || this.childClosed || this.stopRequest?.fatal) return
    const age = this.now() - this.lastLeaseConfirmationAt
    if (
      !Number.isFinite(age)
      || age < 0
      || age > this.heartbeatFreshnessMs
    ) {
      this.logError(
        '[web-singleton] lease confirmation is too old; stopping the web server',
      )
      void this.requestShutdown({ fatal: true })
      return
    }

    try {
      if (!this.child?.connected) throw new Error('IPC disconnected')
      const accepted = this.child.send({
        type: WEB_LEASE_HEARTBEAT_TYPE,
        protocol: WEB_LEASE_HEARTBEAT_PROTOCOL,
      }, (error) => {
        if (error && !this.childClosed && !this.stopRequest) {
          this.logError(
            '[web-singleton] child heartbeat delivery failed; stopping',
          )
          void this.requestShutdown({ fatal: true })
        }
      })
      if (!accepted) throw new Error('IPC backpressure')
    } catch {
      this.logError(
        '[web-singleton] child heartbeat channel is unavailable; stopping',
      )
      void this.requestShutdown({ fatal: true })
      return
    }

    this.heartbeatTimer = this.setTimer(() => {
      this.heartbeatTimer = undefined
      this.sendHeartbeat()
    }, this.heartbeatIntervalMs)
    this.heartbeatTimer?.unref?.()
  }

  signalChild(signal) {
    if (!this.child || this.childClosed) return
    try {
      this.child.kill(signal)
    } catch {
      this.logError(
        '[web-singleton] child signal delivery failed; forcing termination',
      )
    }
  }

  armForceKill(delayMs) {
    if (!this.child || this.childClosed) return
    if (this.forceTimer) this.clearTimer(this.forceTimer)
    this.forceTimer = this.setTimer(() => {
      this.forceTimer = undefined
      if (this.stopRequest) this.stopRequest.fatal = true
      this.clearRenewTimer()
      this.clearHeartbeatTimer()
      this.signalChild('SIGKILL')
      this.hardExitTimer = this.setTimer(() => {
        this.hardExitTimer = undefined
        this.complete({
          exitCode: 1,
          hardExit: true,
          leaseReleased: false,
        })
      }, this.forceKillWaitMs)
      this.hardExitTimer?.unref?.()
    }, delayMs)
    this.forceTimer?.unref?.()
  }

  clearRenewTimer() {
    if (this.renewTimer) this.clearTimer(this.renewTimer)
    this.renewTimer = undefined
  }

  clearHeartbeatTimer() {
    if (this.heartbeatTimer) this.clearTimer(this.heartbeatTimer)
    this.heartbeatTimer = undefined
  }

  clearStopTimers() {
    if (this.forceTimer) this.clearTimer(this.forceTimer)
    if (this.hardExitTimer) this.clearTimer(this.hardExitTimer)
    this.forceTimer = undefined
    this.hardExitTimer = undefined
  }

  async finishWithoutChild() {
    const released = await this.releaseAfterChildExit()
    this.state = 'stopped'
    this.complete({
      exitCode: released && !this.stopRequest?.fatal ? 0 : 1,
      hardExit: false,
      leaseReleased: released,
    })
  }

  async finishAfterChildClose() {
    if (this.completed) return
    this.clearRenewTimer()
    this.clearHeartbeatTimer()
    this.clearStopTimers()
    const released = await this.releaseAfterChildExit()
    const requestedExitCode = this.stopRequest
      ? (this.stopRequest.fatal ? 1 : 0)
      : 1
    if (!released) {
      this.logError(
        '[web-singleton] lease release after child exit was not confirmed',
      )
    }
    if (!this.stopRequest) {
      this.logError('[web-singleton] web server child exited unexpectedly')
    }
    this.state = 'stopped'
    this.complete({
      exitCode: released && requestedExitCode === 0 ? 0 : 1,
      hardExit: false,
      leaseReleased: released,
    })
  }

  async releaseAfterChildExit() {
    if (!this.acquired) return true
    if (this.renewInFlight) {
      try {
        await this.renewInFlight
      } catch {
        // Compare-owner release remains safe after a failed renewal.
      }
    }
    try {
      const released = await this.lease.release()
      this.acquired = false
      return released
    } catch {
      this.acquired = false
      return false
    }
  }

  complete(result) {
    if (this.completed) return
    this.completed = true
    this.resolveCompletion(result)
  }
}
