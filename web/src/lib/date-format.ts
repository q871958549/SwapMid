/**
 * 后端以 UTC 语义下发的时间戳（Hangfire LastExecution/NextExecution、cron 预览等）——
 * 显式按 UTC 解析后转本地时区显示。序列化可能不带 Z 后缀（ABP Clock.Kind 未配），
 * 无时区标记时补 Z 按 UTC 解析。⚠️ 执行历史（ScheduledJobExecution）的时间由后端 IClock
 * （本地语义）写入，不适用本函数——那类字段按原样截断显示即可。
 */
export function fmtUtc(v: string | null): string {
  if (!v) return '—'
  const d = new Date(/Z$|[+-]\d{2}:\d{2}$/.test(v) ? v : v + 'Z')
  if (Number.isNaN(d.getTime())) return v
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`
}

/** 同 {@link fmtUtc} 但精确到秒——表格列出于紧凑只显示到分钟，此函数供 hover title 补秒。 */
export function fmtUtcFull(v: string | null): string {
  if (!v) return ''
  const d = new Date(/Z$|[+-]\d{2}:\d{2}$/.test(v) ? v : v + 'Z')
  if (Number.isNaN(d.getTime())) return ''
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}
