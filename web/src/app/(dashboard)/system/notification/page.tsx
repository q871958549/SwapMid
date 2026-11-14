import { resolvePageShellTitle } from '@/components/data-table/page-shell'
import { PageShellSkeleton } from '@/components/data-table/page-shell-skeleton'
import { Suspense } from 'react'
import { apiFetch } from '@/lib/api-client'
import { NotificationSettingsClient, type NotificationSettings } from './client'

const FALLBACK: NotificationSettings = {
  webhookEnabled: false,
  webhookProvider: 'generic',
  webhookUrl: '',
  webhookSecretConfigured: false,
  webhookMentionUsers: '',
  emailEnabled: false,
  smtpHost: '',
  smtpPort: 465,
  smtpUseSsl: true,
  smtpUserName: '',
  smtpPasswordConfigured: false,
  fromAddress: '',
  fromName: 'SwapMid',
  alertTokenConfigured: false,
  alertWebhookPath: '/api/v1/notification/webhooks/alertmanager',
}

export default async function NotificationSettingsPage() {
  const title = await resolvePageShellTitle('/system/notification')
  return (
    <Suspense fallback={<PageShellSkeleton title={title} />}>
      <NotificationSettingsPageData />
    </Suspense>
  )
}

async function NotificationSettingsPageData() {
  const settings = await apiFetch<NotificationSettings>('/notification/settings').catch((e) => {
    console.error('[notification-settings]', e)
    return FALLBACK
  })
  return <NotificationSettingsClient initial={settings} />
}
