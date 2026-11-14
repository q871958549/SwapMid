import assert from 'node:assert/strict'
import { createServer } from 'node:http'

export async function startOAuthServer() {
  const state = {
    calls: 0,
    delayMs: 0,
    response: 'success',
    expectedRefreshToken: 'refresh-old',
    rejectAfterFirstCall: true,
    successLabel: 'oauth-winner',
  }
  const server = createServer(async (request, response) => {
    if (request.url !== '/connect/token' || request.method !== 'POST') {
      response.writeHead(404).end()
      return
    }
    state.calls += 1
    let requestBody = ''
    for await (const chunk of request) requestBody += chunk
    const refreshToken = new URLSearchParams(requestBody).get('refresh_token')
    if (state.delayMs > 0) {
      await new Promise(resolve => setTimeout(resolve, state.delayMs))
    }
    const invalid = state.response === 'invalid'
      || refreshToken !== state.expectedRefreshToken
      || (state.rejectAfterFirstCall && state.calls > 1)
    response.writeHead(invalid ? 400 : 200, {
      'content-type': 'application/json',
    })
    response.end(JSON.stringify(invalid
      ? { error: 'invalid_grant' }
      : {
          access_token: `access-${state.successLabel}`,
          refresh_token: `refresh-${state.successLabel}`,
          expires_in: 3600,
        }))
  })
  await new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', resolve)
  })
  const address = server.address()
  assert.ok(address && typeof address === 'object')
  return {
    origin: `http://127.0.0.1:${address.port}`,
    state,
    close: () => new Promise((resolve, reject) => {
      server.close(error => error ? reject(error) : resolve())
    }),
  }
}
