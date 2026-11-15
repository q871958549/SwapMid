import { createHmac, timingSafeEqual } from 'node:crypto'

/**
 * Verify SwapMid HTTP Webhook HMAC-SHA256 signature (`X-SwapMid-Signature: sha256=<hex>`).
 * @param {Buffer | string} rawBody Exact raw HTTP request payload
 * @param {string} signatureHeader Value of `x-swapmid-signature`
 * @param {string} sharedSecret Subscription shared secret
 */
export function verifySwapMidWebhook(rawBody, signatureHeader, sharedSecret) {
  if (!signatureHeader || !signatureHeader.startsWith('sha256=')) {
    return false
  }
  const expectedHex = createHmac('sha256', sharedSecret).update(rawBody).digest('hex')
  const actualBuf = Buffer.from(signatureHeader.slice('sha256='.length), 'hex')
  const expectedBuf = Buffer.from(expectedHex, 'hex')
  return actualBuf.length === expectedBuf.length && timingSafeEqual(actualBuf, expectedBuf)
}
