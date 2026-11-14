import { type NextRequest, NextResponse } from 'next/server'
import { getLocale, getTranslations } from 'next-intl/server'
import { sessionUnavailableHeaders } from '@/lib/session-availability'
import { isSafeInternalPath } from '@/lib/safe-internal-path'
import {
  SESSION_UNAVAILABLE_RETRY_SCRIPT,
  sessionUnavailableScriptSrcHash,
} from '../session-unavailable-retry-script'

export const dynamic = 'force-dynamic'

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

export async function GET(req: NextRequest) {
  const redirectTo = req.nextUrl.searchParams.get('redirect')
  const safe = isSafeInternalPath(redirectTo) ? redirectTo : '/dashboard'
  const [t, locale] = await Promise.all([
    getTranslations('ErrorBoundary'),
    getLocale(),
  ])
  const title = escapeHtml(t('title'))
  const description = escapeHtml(t('unavailableDescription'))
  const retry = escapeHtml(t('retry'))
  const retrying = escapeHtml(t('retrying'))
  const retryAgain = escapeHtml(t('retryAgain'))
  const retryHint = escapeHtml(t('retryHint'))
  const href = escapeHtml(safe)
  const lang = escapeHtml(locale)
  const scriptHash = sessionUnavailableScriptSrcHash()

  const html = `<!doctype html>
<html lang="${lang}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${title}</title>
  <style>
    :root { color-scheme: light dark; font-family: system-ui, sans-serif; }
    body { display: grid; min-height: 100vh; margin: 0; place-items: center; background: Canvas; color: CanvasText; }
    main { width: min(32rem, calc(100% - 3rem)); text-align: center; }
    p { color: GrayText; line-height: 1.65; }
    a {
      display: inline-block;
      margin-top: 1rem;
      padding: .7rem 1.25rem;
      border: 1px solid GrayText;
      border-radius: .5rem;
      color: inherit;
      text-decoration: none;
      transition: background-color 150ms, border-color 150ms, opacity 150ms;
    }
    a:hover { background: color-mix(in srgb, CanvasText 8%, Canvas); }
    a:active { background: color-mix(in srgb, CanvasText 14%, Canvas); }
    a:focus-visible { outline: 2px solid Highlight; outline-offset: 2px; }
    a.is-pending { opacity: .7; pointer-events: none; cursor: progress; }
    #hint { margin-top: .75rem; font-size: .875rem; }
    @media (prefers-reduced-motion: reduce) {
      a { transition: none; }
    }
  </style>
</head>
<body>
  <main>
    <h1>${title}</h1>
    <p>${description}</p>
    <a id="retry" href="${href}" data-idle="${retry}" data-pending="${retrying}" data-again="${retryAgain}">${retry}</a>
    <p id="hint" hidden>${retryHint}</p>
  </main>
  <script>${SESSION_UNAVAILABLE_RETRY_SCRIPT}</script>
</body>
</html>`

  return new NextResponse(html, {
    status: 503,
    headers: {
      ...sessionUnavailableHeaders(),
      'Content-Security-Policy': `default-src 'none'; style-src 'unsafe-inline'; script-src ${scriptHash}; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`,
      'Content-Type': 'text/html; charset=utf-8',
      'Referrer-Policy': 'no-referrer',
      'X-Content-Type-Options': 'nosniff',
      'X-Robots-Tag': 'noindex, nofollow',
    },
  })
}
