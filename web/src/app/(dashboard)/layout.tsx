import { redirect } from 'next/navigation'
import { cookies, headers } from 'next/headers'
import { getValidSessionResult } from '@/lib/session'
import {
  redirectSessionFailure,
  redirectSessionIdentityChanged,
} from '@/lib/session-navigation'
import {
  getSessionIdentityVersion,
  getSessionStoragePrincipalId,
} from '@/lib/session-identity'
import { apiFetch } from '@/lib/api-client'
import { canAccessPath } from '@/lib/nav-utils'
import { getBuildId } from '@/lib/build-id'
import { readRuntimeReleaseTag } from '@/lib/runtime-release'
import { Sidebar, SWAPMID_SIDEBAR_BRAND, type NavPreferenceSnapshot } from '@/components/layout/sidebar'
import { MobileNavDrawer } from '@/components/layout/mobile-nav-drawer'
import { Header } from '@/components/layout/header'
import { TabBar } from '@/components/layout/tab-bar'
import { RouteWatcher } from '@/components/layout/route-watcher'
import { ScrollRestorer } from '@/components/layout/scroll-restorer'
import { TablePreferenceCloudSync } from '@/components/layout/table-preference-cloud-sync'
import { SessionGuard } from '@/components/layout/session-guard'
import { ImpersonationBanner } from '@/components/layout/impersonation-banner'
import { ErrorReporter } from '@/components/observability/error-reporter'
import { PermissionsProvider } from '@/components/providers/permissions-provider'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import { Toaster } from '@/components/ui/toaster'

export const dynamic = 'force-dynamic'

export default async function DashboardLayout({ children }: { children: React.ReactNode }) {
  const requestHeaders = await headers()
  const pathname = requestHeaders.get('x-swapmid-pathname') ?? ''
  const sessionResult = await getValidSessionResult()
  if (sessionResult.status === 'identity-changed') {
    return redirectSessionIdentityChanged(pathname)
  }
  const session = sessionResult.status === 'found'
    ? sessionResult.session
    : redirectSessionFailure(sessionResult, pathname)
  const identityVersion = getSessionIdentityVersion(session)
  const storagePrincipalId = getSessionStoragePrincipalId(session)

  const grantedPolicies = session.grantedPolicies
  if (!canAccessPath(pathname, grantedPolicies)) {
    redirect('/dashboard')
  }

  const sidebarCollapsed = (await cookies()).get('swapmid_sidebar_collapsed')?.value === '1'
  const releaseTag = readRuntimeReleaseTag()
  const navPreference = await apiFetch<NavPreferenceSnapshot>('/nav-preference').catch(() => null)

  return (
    <SessionIdentityProvider
      identityVersion={identityVersion}
      storagePrincipalId={storagePrincipalId}
    >
      <SessionGuard identityVersion={identityVersion} />
      <div className="flex h-dvh overflow-hidden bg-canvas pt-[env(safe-area-inset-top)] pb-[env(safe-area-inset-bottom)] md:gap-2 md:p-2 md:pt-[max(0.5rem,env(safe-area-inset-top))] md:pb-[max(0.5rem,env(safe-area-inset-bottom))]">
        <Sidebar
          grantedPolicies={grantedPolicies}
          initialCollapsed={sidebarCollapsed}
          initialNavPreference={navPreference}
          releaseTag={releaseTag}
          brand={SWAPMID_SIDEBAR_BRAND}
        />
        <div className="flex min-w-0 flex-1 flex-col overflow-hidden bg-background md:rounded-xl md:border md:shadow-sm">
          {session.impersonator && (
            <ImpersonationBanner
              targetName={session.name}
              targetUserName={session.userName}
              originatorName={session.impersonator.name || session.impersonator.userName}
            />
          )}
          <Header
            userName={session.userName}
            name={session.name}
            grantedPolicies={grantedPolicies}
            leading={(
              <MobileNavDrawer
                grantedPolicies={grantedPolicies}
                initialNavPreference={navPreference}
                releaseTag={releaseTag}
                brand={SWAPMID_SIDEBAR_BRAND}
              />
            )}
          />
          <TabBar grantedPolicies={grantedPolicies} />
          <RouteWatcher grantedPolicies={grantedPolicies} />
          <ScrollRestorer />
          <TablePreferenceCloudSync />
          <main className="flex min-h-0 flex-1 flex-col overflow-hidden">
            <PermissionsProvider value={grantedPolicies}>
              {children}
            </PermissionsProvider>
          </main>
          <ErrorReporter buildId={getBuildId()} />
        </div>
        <Toaster />
      </div>
    </SessionIdentityProvider>
  )
}
