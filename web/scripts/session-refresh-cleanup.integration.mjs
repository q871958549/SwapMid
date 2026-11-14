import assert from 'node:assert/strict'
import { spawn, spawnSync } from 'node:child_process'
import { randomUUID } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const root = dirname(dirname(fileURLToPath(import.meta.url)))
const probePath = join(root, 'scripts', 'session-refresh-redis.integration.mjs')
const container = `swapmid-session-refresh-it-${randomUUID()}`
const child = spawn(process.execPath, [probePath, '--cleanup-signal-probe'], {
  cwd: root,
  windowsHide: true,
  env: {
    ...process.env,
    SESSION_REFRESH_CONTAINER_NAME: container,
    SESSION_REFRESH_SELF_SIGNAL: process.platform === 'win32' ? '1' : '0',
  },
  stdio: ['ignore', 'pipe', 'pipe'],
})

let stderr = ''
let stdoutBuffer = ''
let sentSignal = false
child.stderr.setEncoding('utf8')
child.stderr.on('data', chunk => {
  stderr += chunk
})

const ready = new Promise((resolve, reject) => {
  const timer = setTimeout(
    () => reject(new Error(`cleanup probe did not start: ${stderr}`)),
    20_000,
  )
  child.stdout.setEncoding('utf8')
  child.stdout.on('data', (chunk) => {
    stdoutBuffer += chunk
    const lines = stdoutBuffer.split(/\r?\n/)
    stdoutBuffer = lines.pop() ?? ''
    for (const line of lines) {
      if (!line.startsWith('{')) continue
      let event
      try {
        event = JSON.parse(line)
      } catch (error) {
        clearTimeout(timer)
        reject(error)
        return
      }
      if (event.event !== 'cleanup-probe-ready') continue
      if (event.container !== container) {
        clearTimeout(timer)
        reject(new Error('cleanup probe reported an unexpected container'))
        return
      }
      clearTimeout(timer)
      resolve()
    }
  })
})

const exited = new Promise((resolve, reject) => {
  child.once('error', reject)
  child.once('exit', (code, signal) => resolve({ code, signal }))
})

try {
  await ready
  assert.match(container, /^swapmid-session-refresh-it-[0-9a-f-]+$/)
  if (process.platform !== 'win32') {
    sentSignal = child.kill('SIGTERM')
    assert.equal(sentSignal, true)
  }
  const exit = await new Promise((resolve, reject) => {
    const timer = setTimeout(
      () => reject(new Error('cleanup probe ignored SIGTERM')),
      15_000,
    )
    exited.then(
      value => {
        clearTimeout(timer)
        resolve(value)
      },
      error => {
        clearTimeout(timer)
        reject(error)
      },
    )
  })
  assert.equal(exit.code, 143)
  const inspect = spawnSync('docker', ['inspect', container], {
    cwd: root,
    encoding: 'utf8',
    windowsHide: true,
  })
  assert.notEqual(inspect.status, 0, 'SIGTERM 后临时 Redis 容器仍然存在')
  process.stdout.write('session refresh SIGTERM cleanup integration: PASS\n')
} finally {
  if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL')
  if (container) {
    spawnSync('docker', ['rm', '-f', container], {
      cwd: root,
      encoding: 'utf8',
      windowsHide: true,
      timeout: 10_000,
    })
  }
}
