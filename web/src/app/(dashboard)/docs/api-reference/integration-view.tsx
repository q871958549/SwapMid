// 「集成指南」视图：渲染单个对外集成场景（IntegrationGuide）的场景化文档。
// 从 client.tsx 抽出，避免 client.tsx 触 max-lines 护栏；内容数据见 integration-guides.ts
// （正文按拍板登记「有意不翻」，此处只翻译框架文案——受众/鉴权/调用步骤/请求响应示例/注意事项等标签）。
import { useTranslations } from 'next-intl'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'
import type { IntegrationGuide, IntegrationStep } from './types'

const METHOD_STYLE: Record<string, string> = {
  GET: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300',
  POST: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300',
  PUT: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300',
  DELETE: 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300',
  PATCH: 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300',
}

function CodeBlock({ label, code }: { label: string; code: string }) {
  return (
    <div className="mt-2.5">
      <div className="mb-1 text-xs font-medium text-muted-foreground">{label}</div>
      <pre className="overflow-x-auto whitespace-pre rounded-lg border bg-muted/40 p-3 font-mono text-xs leading-relaxed">
        {code}
      </pre>
    </div>
  )
}

function StepCard({ step }: { step: IntegrationStep }) {
  const t = useTranslations('DeveloperDocs.apiReference')
  return (
    <div className="rounded-lg border bg-card p-4">
      <div className="flex items-center gap-3">
        <span className={cn('shrink-0 rounded px-2 py-0.5 text-center text-xs font-bold', METHOD_STYLE[step.method])} style={{ width: '3.5rem' }}>
          {step.method}
        </span>
        <code className="break-all font-mono text-sm">{step.path}</code>
      </div>
      <div className="mt-2 text-sm font-medium">{step.title}</div>
      <p className="mt-1 text-sm text-muted-foreground">{step.description}</p>
      {step.auth && <p className="mt-1 text-xs text-muted-foreground">{t('authLabel')}{step.auth}</p>}
      {step.request && <CodeBlock label={t('requestExampleLabel')} code={step.request} />}
      {step.response && <CodeBlock label={t('responseExampleLabel')} code={step.response} />}
      {step.note && (
        <div className="mt-2.5 flex gap-2 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-300">
          <span className="shrink-0 font-medium">提示</span>
          <span>{step.note}</span>
        </div>
      )}
    </div>
  )
}

export function IntegrationView({ guide }: { guide: IntegrationGuide }) {
  const t = useTranslations('DeveloperDocs.apiReference')
  return (
    <div className="mx-auto max-w-4xl space-y-6 px-6 py-6">
      <div>
        <h2 className="text-xl font-semibold">{guide.title}</h2>
        <p className="mt-1 text-sm text-muted-foreground">{guide.intro}</p>
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <Badge variant="outline" className="font-mono text-xs">{guide.baseUrl}</Badge>
          <Badge variant="secondary" className="text-xs">{t('audienceLabel')}{guide.audience}</Badge>
          <Badge variant="secondary" className="text-xs">{t('authLabel')}{guide.auth}</Badge>
        </div>
      </div>

      <div>
        <h3 className="mb-2 font-mono text-sm font-semibold text-muted-foreground">{t('callStepsTitle')}</h3>
        <div className="space-y-3">
          {guide.steps.map((step) => (
            <StepCard key={`${step.method}-${step.path}`} step={step} />
          ))}
        </div>
      </div>

      {guide.notes && guide.notes.length > 0 && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 dark:border-amber-800 dark:bg-amber-900/20">
          <div className="mb-2 text-sm font-medium text-amber-800 dark:text-amber-300">{t('notesTitle')}</div>
          <ul className="space-y-1.5 text-sm text-amber-800 dark:text-amber-300">
            {guide.notes.map((note, i) => (
              <li key={i} className="flex gap-2">
                <span className="shrink-0">•</span>
                <span className="leading-relaxed">{note}</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
