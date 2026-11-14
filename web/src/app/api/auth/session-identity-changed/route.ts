import { type NextRequest, NextResponse } from 'next/server'
import { isSafeInternalPath } from '@/lib/safe-internal-path'

export const dynamic = 'force-dynamic'

/**
 * 身份已经在服务端完成切换，当前请求被拒绝后经这里做一次不可缓存的整页重建。
 * 保留 swapmid_sid；它现在指向的是有效的新身份会话。
 */
export async function GET(req: NextRequest) {
  const redirectTo = req.nextUrl.searchParams.get('redirect')
  const location = isSafeInternalPath(redirectTo) ? redirectTo : '/dashboard'

  return new NextResponse(null, {
    status: 307,
    headers: {
      Location: location,
      'Cache-Control': 'private, no-store',
    },
  })
}
