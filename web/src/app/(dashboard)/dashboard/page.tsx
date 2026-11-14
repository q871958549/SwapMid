import { Suspense } from 'react'
import { getTranslations } from 'next-intl/server'
import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { PageShellSkeleton } from '@/components/data-table/page-shell-skeleton'
import { getValidSessionResult } from '@/lib/session'
import { redirectSessionFailure, redirectSessionIdentityChanged } from '@/lib/session-navigation'
import { Card } from '@/components/ui/card'
import { PageLink } from '@/components/layout/page-link'
import { ScrollPageShell } from '@/components/layout/scroll-page-shell'
import { resolveDomains, translateNavDescription, translateNavTitle, type NavT } from '@/lib/nav-utils'
import { apiFetch } from '@/lib/api-client'
import { applyNavOrder, parseNavOrder } from '@/lib/nav-order'
import { parseDomainPrefs, splitDomains } from '@/lib/domain-prefs'
import type { NavPreferenceSnapshot } from '@/components/layout/sidebar'
import { OpsStrip, OpsStripSkeleton } from './ops-strip'
import { RecentVisits } from './recent-visits'

export default async function DashboardPage() {
  const title = await resolvePageShellTitle('/dashboard')
  return (
    <Suspense fallback={<PageShellSkeleton title={title} />}>
      <DashboardPageData />
    </Suspense>
  )
}

async function DashboardPageData() {
  const result = await getValidSessionResult()
  if (result.status === 'identity-changed') return redirectSessionIdentityChanged('/dashboard')
  const session = result.status === 'found' ? result.session : redirectSessionFailure(result, '/dashboard')
  const grantedPolicies = session.grantedPolicies
  const navPreference = await apiFetch<NavPreferenceSnapshot>('/nav-preference').catch(() => null)
  const navOrder = parseNavOrder(navPreference?.value ?? null)
  const domains = splitDomains(resolveDomains(grantedPolicies), parseDomainPrefs(navPreference?.domainPrefs), (d) => d.domain.key)
    .shown.map((resolved) => ({ ...resolved, groups: applyNavOrder(resolved.groups, navOrder) }))
  const t = (await getTranslations('Nav')) as NavT
  const td = await getTranslations('Dashboard')

  const hasOpsAccess = [
    'SwapMid.SystemManagement.SystemHealth',
    'SwapMid.SystemManagement.ScheduledJobs',
    'SwapMid.MessageBus.Records',
  ].some((p) => grantedPolicies[p] === true)

  const hour = Number(
    new Intl.DateTimeFormat('en-US', { timeZone: 'Asia/Shanghai', hour: '2-digit', hourCycle: 'h23' })
      .format(new Date()),
  )
  const greetingKey = hour < 12 ? 'greetingMorning' : hour < 18 ? 'greetingAfternoon' : 'greetingEvening'
  const displayName = session.name || session.userName || ''

  return (
    <ScrollPageShell
      header={(
        <div>
          <h1 className="text-2xl font-bold tracking-tight">{td(greetingKey, { name: displayName })}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{td('subtitle')}</p>
        </div>
      )}
    >
      <div className="flex flex-col gap-5">
        {hasOpsAccess && (
          <Suspense fallback={<OpsStripSkeleton />}>
            <OpsStrip grantedPolicies={grantedPolicies} />
          </Suspense>
        )}

        <RecentVisits grantedPolicies={grantedPolicies} />

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {domains.map(({ domain, groups }) => {
            const Icon = domain.icon
            const items = groups.flatMap((g) => g.items)
            const firstHref = items[0]?.href ?? '/dashboard'
            return (
              <Card key={domain.key} className="flex h-full flex-col gap-3 p-5">
                <PageLink href={firstHref} className="group/title flex items-center gap-3">
                  <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 transition-colors group-hover/title:bg-primary/15">
                    <Icon className="size-5 text-primary" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <h3 className="font-semibold transition-colors group-hover/title:text-primary">{translateNavTitle(domain, t)}</h3>
                    <p className="truncate text-xs text-muted-foreground">{translateNavDescription(domain, t)}</p>
                  </div>
                </PageLink>
                <div className="flex flex-wrap gap-1.5">
                  {groups.map((g) => (
                    <PageLink
                      key={g.key}
                      href={g.items[0]?.href ?? firstHref}
                      className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground transition-colors hover:bg-primary/10 hover:text-primary"
                    >
                      {translateNavTitle(g, t)}
                    </PageLink>
                  ))}
                </div>
              </Card>
            )
          })}
        </div>
      </div>
    </ScrollPageShell>
  )
}
