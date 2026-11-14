'use client'

import { useState } from 'react'
import { useTranslations } from 'next-intl'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { toast } from '@/components/ui/use-toast'
import { getApiErrorMessage } from '@/lib/api-error'

export interface NotificationSettings {
  webhookEnabled: boolean
  webhookProvider: string
  webhookUrl: string
  webhookSecretConfigured: boolean
  webhookMentionUsers: string
  emailEnabled: boolean
  smtpHost: string
  smtpPort: number
  smtpUseSsl: boolean
  smtpUserName: string
  smtpPasswordConfigured: boolean
  fromAddress: string
  fromName: string
  alertTokenConfigured: boolean
  alertWebhookPath: string
}

const CHANNEL_WEBHOOK = 1
const CHANNEL_EMAIL = 2

const WEBHOOK_PROVIDERS = [
  { value: 'generic', labelKey: 'webhookCard.providerGeneric' },
  { value: 'slack', labelKey: 'webhookCard.providerSlack' },
  { value: 'feishu', labelKey: 'webhookCard.providerFeishu' },
  { value: 'dingtalk', labelKey: 'webhookCard.providerDingtalk' },
  { value: 'wecom', labelKey: 'webhookCard.providerWecom' },
] as const

export function NotificationSettingsClient({ initial }: { initial: NotificationSettings }) {
  const t = useTranslations('System.notification')
  const tc = useTranslations('Common')
  const [form, setForm] = useState(initial)
  const [webhookSecret, setWebhookSecret] = useState('')
  const [smtpPassword, setSmtpPassword] = useState('')
  const [testEmail, setTestEmail] = useState('')
  const [generatedAlertToken, setGeneratedAlertToken] = useState('')
  const [saving, setSaving] = useState(false)
  const [testingWebhook, setTestingWebhook] = useState(false)
  const [testingEmail, setTestingEmail] = useState(false)
  const [genToken, setGenToken] = useState(false)

  async function save() {
    setSaving(true)
    try {
      const res = await fetch('/api/proxy/notification/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          webhookEnabled: form.webhookEnabled,
          webhookProvider: form.webhookProvider,
          webhookUrl: form.webhookUrl,
          webhookSecret: webhookSecret || undefined,
          webhookMentionUsers: form.webhookMentionUsers,
          emailEnabled: form.emailEnabled,
          smtpHost: form.smtpHost,
          smtpPort: form.smtpPort,
          smtpUseSsl: form.smtpUseSsl,
          smtpUserName: form.smtpUserName,
          smtpPassword: smtpPassword || undefined,
          fromAddress: form.fromAddress,
          fromName: form.fromName,
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      setForm((f) => ({
        ...f,
        webhookSecretConfigured: f.webhookSecretConfigured || !!webhookSecret,
        smtpPasswordConfigured: f.smtpPasswordConfigured || !!smtpPassword,
      }))
      setWebhookSecret('')
      setSmtpPassword('')
      toast({ variant: 'success', title: t('toastSavedTitle') })
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('toastSaveFailedTitle'), description: getApiErrorMessage(e, t('toastSaveFailedFallback')) })
    } finally {
      setSaving(false)
    }
  }

  async function sendTest(channel: number, toUser: string | null) {
    const isEmail = channel === CHANNEL_EMAIL
    const setBusy = isEmail ? setTestingEmail : setTestingWebhook
    setBusy(true)
    try {
      const res = await fetch('/api/proxy/notification/settings/test', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ channel, toUser }),
      })
      if (!res.ok) throw new Error(await res.text())
      const result = (await res.json()) as { success: boolean; error?: string }
      if (result.success) {
        toast({ variant: 'success', title: t('toastTestSentTitle'), description: isEmail ? t('toastTestSentEmailDesc') : t('toastTestSentWebhookDesc') })
      } else {
        toast({ variant: 'destructive', title: t('toastTestFailedTitle'), description: result.error ?? t('toastTestFailedFallback') })
      }
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('toastTestFailedTitle'), description: getApiErrorMessage(e, t('toastTestFailedFallbackNoConfig')) })
    } finally {
      setBusy(false)
    }
  }

  async function regenToken() {
    setGenToken(true)
    try {
      const res = await fetch('/api/proxy/notification/settings/alert-token', { method: 'POST' })
      if (!res.ok) throw new Error(await res.text())
      const raw = (await res.text()).trim()
      let token = raw
      try {
        const parsed: unknown = JSON.parse(raw)
        if (typeof parsed === 'string') token = parsed.trim()
      } catch { /* raw text string */ }
      setGeneratedAlertToken(token)
      setForm((f) => ({ ...f, alertTokenConfigured: true }))
      toast({ variant: 'success', title: t('toastTokenGeneratedTitle'), description: t('toastTokenGeneratedDesc') })
    } catch (e) {
      console.error(e)
      toast({ variant: 'destructive', title: t('toastTokenGenFailedTitle'), description: getApiErrorMessage(e, t('toastSaveFailedFallback')) })
    } finally {
      setGenToken(false)
    }
  }

  const webhookCurlExample = generatedAlertToken
    ? `curl -X POST http://api:8080${form.alertWebhookPath} -H "X-SwapMid-Alert-Token: ${generatedAlertToken}" -H "Content-Type: application/json" -d '{"alerts":[]}'`
    : `http://api:8080${form.alertWebhookPath}`

  return (
    <div className="max-w-3xl space-y-6">
      <Card>
        <CardHeader>
          <CardTitle>{t('emailCard.title')}</CardTitle>
          <CardDescription>{t('emailCard.description')}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium">{t('emailCard.enableLabel')}</p>
              <p className="text-muted-foreground text-xs">{t('emailCard.enableHint')}</p>
            </div>
            <Switch checked={form.emailEnabled} onCheckedChange={(v) => setForm((f) => ({ ...f, emailEnabled: v }))} />
          </div>

          <div className="grid grid-cols-[1fr_120px_auto] items-end gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="smtp-host">{t('emailCard.smtpHost')}</Label>
              <Input id="smtp-host" value={form.smtpHost} onChange={(e) => setForm((f) => ({ ...f, smtpHost: e.target.value }))} placeholder="smtp.example.com" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="smtp-port">{t('emailCard.smtpPort')}</Label>
              <Input id="smtp-port" type="number" value={form.smtpPort} onChange={(e) => setForm((f) => ({ ...f, smtpPort: Number(e.target.value) || 0 }))} placeholder="465" />
            </div>
            <div className="flex h-9 items-center gap-2 pb-0.5">
              <Switch id="smtp-ssl" checked={form.smtpUseSsl} onCheckedChange={(v) => setForm((f) => ({ ...f, smtpUseSsl: v }))} />
              <Label htmlFor="smtp-ssl" className="whitespace-nowrap text-xs">{t('emailCard.sslLabel')}</Label>
            </div>
          </div>
          <p className="text-muted-foreground -mt-3 text-xs">{t('emailCard.sslHint')}</p>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="email-from">{t('emailCard.emailFrom')}</Label>
              <Input id="email-from" value={form.fromAddress} onChange={(e) => setForm((f) => ({ ...f, fromAddress: e.target.value }))} placeholder="alert@example.com" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="email-from-name">{t('emailCard.emailFromName')}</Label>
              <Input id="email-from-name" value={form.fromName} onChange={(e) => setForm((f) => ({ ...f, fromName: e.target.value }))} placeholder="SwapMid" />
            </div>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="email-username">{t('emailCard.emailUserName')}</Label>
              <Input id="email-username" value={form.smtpUserName} onChange={(e) => setForm((f) => ({ ...f, smtpUserName: e.target.value }))} placeholder={t('emailCard.emailUserNamePlaceholder')} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="email-password">{t('emailCard.emailPassword')}</Label>
              <Input
                id="email-password"
                type="password"
                value={smtpPassword}
                onChange={(e) => setSmtpPassword(e.target.value)}
                placeholder={form.smtpPasswordConfigured ? t('emailCard.emailPasswordConfiguredPlaceholder') : t('emailCard.emailPasswordPlaceholder')}
              />
            </div>
          </div>

          <div className="flex flex-wrap items-end gap-2">
            <Button onClick={() => void save()} disabled={saving}>
              {saving ? tc('saving') : tc('save')}
            </Button>
            <div className="flex items-end gap-2">
              <div className="space-y-1.5">
                <Label htmlFor="test-email" className="text-xs">{t('emailCard.testEmailLabel')}</Label>
                <Input id="test-email" value={testEmail} onChange={(e) => setTestEmail(e.target.value)} placeholder="you@example.com" className="h-9 w-56" />
              </div>
              <Button
                variant="outline"
                onClick={() => void sendTest(CHANNEL_EMAIL, testEmail)}
                disabled={testingEmail || !testEmail.trim()}
              >
                {testingEmail ? t('sending') : t('emailCard.testSend')}
              </Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('webhookCard.title')}</CardTitle>
          <CardDescription>{t('webhookCard.description')}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium">{t('webhookCard.enableLabel')}</p>
              <p className="text-muted-foreground text-xs">{t('webhookCard.enableHint')}</p>
            </div>
            <Switch checked={form.webhookEnabled} onCheckedChange={(v) => setForm((f) => ({ ...f, webhookEnabled: v }))} />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>{t('webhookCard.providerLabel')}</Label>
              <Select value={form.webhookProvider} onValueChange={(v) => setForm((f) => ({ ...f, webhookProvider: v }))}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {WEBHOOK_PROVIDERS.map((p) => (
                    <SelectItem key={p.value} value={p.value}>{t(p.labelKey)}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="webhook-mentions">{t('webhookCard.mentionUsersLabel')}</Label>
              <Input
                id="webhook-mentions"
                value={form.webhookMentionUsers}
                onChange={(e) => setForm((f) => ({ ...f, webhookMentionUsers: e.target.value }))}
                placeholder={t('webhookCard.mentionUsersPlaceholder')}
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="webhook-url">{t('webhookCard.urlLabel')}</Label>
            <Input
              id="webhook-url"
              value={form.webhookUrl}
              onChange={(e) => setForm((f) => ({ ...f, webhookUrl: e.target.value }))}
              placeholder="https://hooks.example.com/services/..."
              className="font-mono text-xs"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="webhook-secret">{t('webhookCard.secretLabel')}</Label>
            <Input
              id="webhook-secret"
              type="password"
              value={webhookSecret}
              onChange={(e) => setWebhookSecret(e.target.value)}
              placeholder={form.webhookSecretConfigured ? t('webhookCard.secretConfiguredPlaceholder') : t('webhookCard.secretPlaceholder')}
            />
          </div>

          <div className="flex gap-2">
            <Button onClick={() => void save()} disabled={saving}>
              {saving ? tc('saving') : tc('save')}
            </Button>
            <Button variant="outline" onClick={() => void sendTest(CHANNEL_WEBHOOK, null)} disabled={testingWebhook || !form.webhookUrl.trim()}>
              {testingWebhook ? t('sending') : t('emailCard.testSend')}
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t('alertCard.title')}</CardTitle>
          <CardDescription>{t('alertCard.description')}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="space-y-1.5">
            <Label>{t('alertCard.tokenLabel')}</Label>
            <div className="flex gap-2">
              <Input
                readOnly
                value={
                  generatedAlertToken
                    || (form.alertTokenConfigured ? t('alertCard.tokenConfiguredMasked') : t('alertCard.tokenNotGenerated'))
                }
                className="font-mono"
              />
              <Button variant="outline" onClick={() => void regenToken()} disabled={genToken}>
                {genToken ? t('alertCard.generating') : form.alertTokenConfigured ? t('alertCard.rotate') : t('alertCard.generate')}
              </Button>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label>{t('alertCard.webhookUrlLabel')}</Label>
            <Input readOnly value={webhookCurlExample} className="font-mono text-xs" />
            <p className="text-muted-foreground text-xs">
              {t('alertCard.webhookUrlHint')}
            </p>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
