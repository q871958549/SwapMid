// @vitest-environment node

import { once } from 'node:events'
import { createServer, type Server } from 'node:http'
import type { AddressInfo } from 'node:net'
import { afterEach, describe, expect, it } from 'vitest'
import { limitRequestBody } from './backend-forwarding'

const servers: Server[] = []

afterEach(async () => {
  await Promise.all(servers.splice(0).map(server => new Promise<void>(resolve => {
    server.closeAllConnections()
    server.close(() => resolve())
  })))
})

async function listen(server: Server): Promise<string> {
  servers.push(server)
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')
  const address = server.address() as AddressInfo
  return `http://127.0.0.1:${address.port}`
}

describe('backend forwarding · Node/Undici 半双工回归', () => {
  it('上游提前返回流式 400 时，优雅停止上传仍可完整读取响应正文', async () => {
    const server = createServer((request, response) => {
      request.once('data', () => {
        response.writeHead(400, { 'Content-Type': 'text/plain; charset=utf-8' })
        response.write('early-')
        setTimeout(() => response.end('response'), 25)
      })
    })
    server.on('clientError', (_error, socket) => socket.destroy())
    const baseUrl = await listen(server)

    let markSourceCancelled: (() => void) | undefined
    const sourceCancelled = new Promise<void>(resolve => {
      markSourceCancelled = resolve
    })
    const source = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode('first-chunk'))
      },
      cancel() {
        markSourceCancelled!()
      },
    })
    const gracefulStopController = new AbortController()
    const requestBody = limitRequestBody(source, 1024, new AbortController(), {
      gracefulStopSignal: gracefulStopController.signal,
    })

    const response = await fetch(`${baseUrl}/early-response`, {
      method: 'POST',
      body: requestBody,
      duplex: 'half',
    } as RequestInit & { duplex: 'half' })
    expect(response.status).toBe(400)

    gracefulStopController.abort(
      new DOMException('upstream final response received', 'AbortError'),
    )

    await expect(response.text()).resolves.toBe('early-response')
    await sourceCancelled
  }, 10_000)
})
