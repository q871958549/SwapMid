import type { Metadata, Viewport } from 'next'
import Script from 'next/script'
import { cookies } from 'next/headers'
import { NextIntlClientProvider } from 'next-intl'
import { getLocale, getTranslations } from 'next-intl/server'
import { IntlObservabilityProvider } from '@/components/providers/intl-observability-provider'
import { ThemeProvider } from '@/components/theme-provider'
import { getBuildId } from '@/lib/build-id'
import { readRuntimeReleaseTag } from '@/lib/runtime-release'
import { VersionWatcher } from '@/components/layout/version-watcher'
import './globals.css'

// i18n 例外登记（批次4k扫描仪表已排查，勿删）：SITE_TITLE 是品牌名，与登录页 <h1>/侧栏 logo 文案
// 字面完全一致、全站从未翻译过（专有名词，见 sidebar.tsx 同批次登记）；description 是通用副标题，
// 随语言翻译（Common.platformTagline，与登录页 login/client.tsx 的 tagline 共用同一 key）。
const SITE_TITLE = 'SwapMid'

export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1,
  viewportFit: 'cover',
  colorScheme: 'light dark',
}

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations('Common')
  return { title: SITE_TITLE, description: t('platformTagline') }
}

type Theme = 'light' | 'dark' | 'system'

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  // 从 swapmid_theme / swapmid_font_size cookie（登录时写入的账号偏好镜像）读初值，服务端首屏直出防主题闪白。
  // 白名单/范围校验后再注入内联脚本，杜绝 cookie 被改后经 <script> 注入（XSS）。
  const cookieStore = await cookies()
  const rawTheme = cookieStore.get('swapmid_theme')?.value
  const theme: Theme | '' = rawTheme === 'light' || rawTheme === 'dark' || rawTheme === 'system' ? rawTheme : ''
  const rawFontSize = Number(cookieStore.get('swapmid_font_size')?.value)
  const fontSize = rawFontSize >= 13 && rawFontSize <= 18 ? rawFontSize : 0
  const locale = await getLocale()

  return (
    <html lang={locale === 'en' ? 'en' : locale === 'zh-Hant' ? 'zh-TW' : 'zh-CN'} suppressHydrationWarning>
      <body className="antialiased">
        {/*
          next/script beforeInteractive 在 React 树外注入、hydration 前执行，避免主题闪烁（FOUC）。
          服务端 cookie 值（已白名单/范围校验）优先，未登录/无 cookie 回退 localStorage，再回退 system。
        */}
        <Script id="theme-init" strategy="beforeInteractive">{`
          try {
            var t = ${JSON.stringify(theme)} || localStorage.getItem('swapmid_theme') || 'system';
            var dark = t === 'dark' || (t === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
            document.documentElement.classList.toggle('dark', dark);
            var fs = ${fontSize} || Number(localStorage.getItem('swapmid_font_size'));
            if (fs >= 13 && fs <= 18) document.documentElement.style.fontSize = fs + 'px';
          } catch(e) {}
        `}</Script>
        <NextIntlClientProvider>
          <VersionWatcher initialBuildId={getBuildId()} initialRelease={readRuntimeReleaseTag()} />
          <IntlObservabilityProvider buildId={getBuildId()}>
            <ThemeProvider initialTheme={theme || undefined} initialFontSize={fontSize || undefined}>
              {children}
            </ThemeProvider>
          </IntlObservabilityProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  )
}
