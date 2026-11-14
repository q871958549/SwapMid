import { describe, expect, it } from 'vitest'
import {
  createI18nIncident,
  formatI18nIncident,
  getSafeMessageFallback,
  resolveMessageKey,
} from './runtime'

describe('i18n runtime contract', () => {
  it('缺失消息回退不暴露原始点号 key', () => {
    expect(getSafeMessageFallback('zh-Hans')).toBe('内容暂不可用')
    expect(getSafeMessageFallback('zh-Hant')).toBe('內容暫不可用')
    expect(getSafeMessageFallback('en')).toBe('Content unavailable')
    expect(getSafeMessageFallback('unknown')).not.toContain('ProjectManagement.')
  })

  it('可观测事件固定携带 locale/key/path/buildId', () => {
    const incident = createI18nIncident({
      code: 'MISSING_MESSAGE',
      locale: 'zh-Hans',
      namespace: 'ProjectManagement.projectDetail',
      key: 'colNodePersonnel',
      path: '/project-management/projects/p-1',
      buildId: 'build-123',
    })

    expect(resolveMessageKey('ProjectManagement.projectDetail', 'colNodePersonnel'))
      .toBe('ProjectManagement.projectDetail.colNodePersonnel')
    expect(JSON.parse(formatI18nIncident(incident))).toEqual(incident)
  })
})
