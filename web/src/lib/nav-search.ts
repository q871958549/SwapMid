import { pinyin } from 'pinyin-pro'
import { pinyinInitials } from '@/components/data-table/data-table-filters'

// 字段之间 / 同一字段多文案之间的分隔符，避免再拼成「一锅粥」给模糊匹配借字母。
const FIELD_SEP = '\u001f'
const VALUE_SEP = '\u001e'

const ASCII_QUERY = /^[a-z0-9]+$/
const HREF_QUERY = /^[a-z0-9/_-]+$/

export const NAV_SEARCH_SCORE = {
  titleInitialPrefix: 1,
  titleInitialContains: 0.9,
  titleText: 0.8,
  groupInitialPrefix: 0.5,
  groupInitialContains: 0.4,
  groupText: 0.3,
  hrefSegment: 0.2,
} as const

export interface NavSearchDoc {
  titles: string[]
  groups: string[]
  href: string
}

const _fullPinyinCache = new Map<string, string>()

function fullPinyin(text: string): string {
  let cached = _fullPinyinCache.get(text)
  if (cached === undefined) {
    cached = pinyin(text, { toneType: 'none', type: 'string' }).replace(/\s/g, '').toLowerCase()
    _fullPinyinCache.set(text, cached)
  }
  return cached
}

function uniqueNonEmpty(values: string[]): string[] {
  const seen = new Set<string>()
  const out: string[] = []
  for (const value of values) {
    const trimmed = value.trim()
    if (!trimmed || seen.has(trimmed)) continue
    seen.add(trimmed)
    out.push(trimmed)
  }
  return out
}

export function buildNavSearchDoc(input: {
  title: string
  titleZh?: string
  group: string
  groupZh?: string
  /** 所属业务域：与组同属「归属」字段、同一档分数，让「搜域名」也能找到该域下的页面（蓝图 P1-6）。 */
  domain?: string
  domainZh?: string
  href: string
}): NavSearchDoc {
  return {
    titles: uniqueNonEmpty([input.title, input.titleZh ?? '']),
    groups: uniqueNonEmpty([input.group, input.groupZh ?? '', input.domain ?? '', input.domainZh ?? '']),
    href: input.href,
  }
}

export function formatNavSearchValue(doc: NavSearchDoc): string {
  return [doc.href, doc.titles.join(VALUE_SEP), doc.groups.join(VALUE_SEP)].join(FIELD_SEP)
}

export function parseNavSearchValue(value: string): NavSearchDoc {
  const [href = '', titlePart = '', groupPart = ''] = value.split(FIELD_SEP)
  return {
    href,
    titles: titlePart.split(VALUE_SEP).filter(Boolean),
    groups: groupPart.split(VALUE_SEP).filter(Boolean),
  }
}

function scoreText(text: string, query: string, ascii: boolean): {
  initialPrefix: boolean
  initialContains: boolean
  text: boolean
} {
  const lower = text.toLowerCase()
  const initials = pinyinInitials(text)
  return {
    initialPrefix: ascii && initials.startsWith(query),
    initialContains: ascii && initials.includes(query),
    text: lower.includes(query) || (ascii && fullPinyin(text).includes(query)),
  }
}

function maxScore(
  texts: string[],
  query: string,
  ascii: boolean,
  scores: { prefix: number; contains: number; text: number },
): number {
  let best = 0
  for (const text of texts) {
    const hit = scoreText(text, query, ascii)
    if (hit.initialPrefix) best = Math.max(best, scores.prefix)
    else if (hit.initialContains) best = Math.max(best, scores.contains)
    if (hit.text) best = Math.max(best, scores.text)
  }
  return best
}

export function scoreNavSearch(doc: NavSearchDoc, rawQuery: string): number {
  const query = rawQuery.trim().toLowerCase()
  if (!query) return 1

  const ascii = ASCII_QUERY.test(query)
  let best = maxScore(doc.titles, query, ascii, {
    prefix: NAV_SEARCH_SCORE.titleInitialPrefix,
    contains: NAV_SEARCH_SCORE.titleInitialContains,
    text: NAV_SEARCH_SCORE.titleText,
  })
  best = Math.max(
    best,
    maxScore(doc.groups, query, ascii, {
      prefix: NAV_SEARCH_SCORE.groupInitialPrefix,
      contains: NAV_SEARCH_SCORE.groupInitialContains,
      text: NAV_SEARCH_SCORE.groupText,
    }),
  )

  if (HREF_QUERY.test(query)) {
    const segments = doc.href.toLowerCase().split('/').filter(Boolean)
    if (segments.some((segment) => segment === query || segment.startsWith(query))) {
      best = Math.max(best, NAV_SEARCH_SCORE.hrefSegment)
    }
  }

  return best
}

/** cmdk 自定义 filter：按字段打分，分数 > 0 才展示。 */
export function navCommandFilter(value: string, search: string): number {
  return scoreNavSearch(parseNavSearchValue(value), search)
}
