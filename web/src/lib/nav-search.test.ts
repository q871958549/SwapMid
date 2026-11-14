import { describe, expect, it } from 'vitest'
import { navGroups } from '@/components/layout/nav-config'
import { pinyinInitials } from '@/components/data-table/data-table-filters'
import {
  buildNavSearchDoc,
  formatNavSearchValue,
  NAV_SEARCH_SCORE,
  navCommandFilter,
  parseNavSearchValue,
  scoreNavSearch,
} from './nav-search'

function doc(title: string, group: string, href: string, titleZh = title, groupZh = group) {
  return buildNavSearchDoc({ title, titleZh, group, groupZh, href })
}

const pipeline = doc('数据管道', '数据集成', '/data-integration/pipelines')
const myPipeline = doc('我的数据管道', '数据集成', '/data-integration/my-pipelines')
const dict = doc('数据字典', '系统管理', '/system/data-dictionaries')
const codeRule = doc('编码规则', '系统管理', '/system/code-rules')
const employee = doc('员工', '企业管理', '/enterprise-management/employees')
const topic = doc('消息主题', '消息总线', '/message-bus/topics')

describe('buildNavSearchDoc', () => {
  it('中英标题去重后都保留，拼音仍能打中文原文', () => {
    const built = buildNavSearchDoc({
      title: 'Pipelines',
      titleZh: '数据管道',
      group: 'Data Integration',
      groupZh: '数据集成',
      href: '/data-integration/pipelines',
    })
    expect(built.titles).toEqual(['Pipelines', '数据管道'])
    expect(scoreNavSearch(built, 'sjgd')).toBe(NAV_SEARCH_SCORE.titleInitialPrefix)
    expect(scoreNavSearch(built, 'pipelines')).toBe(NAV_SEARCH_SCORE.titleText)
  })

  it('带上所属业务域后，搜域名能找到页面，分数与组同档、低于标题命中', () => {
    const built = buildNavSearchDoc({
      title: '数据管道',
      group: '数据集成',
      domain: '集成引擎',
      href: '/data-integration/pipelines',
    })
    expect(scoreNavSearch(built, 'jcyq')).toBe(NAV_SEARCH_SCORE.groupInitialPrefix)
    expect(scoreNavSearch(built, '引擎')).toBe(NAV_SEARCH_SCORE.groupText)
    expect(scoreNavSearch(built, 'sjgd')).toBe(NAV_SEARCH_SCORE.titleInitialPrefix)
  })
})

describe('format / parse 往返', () => {
  it('字段隔离，不把分组和标题粘成一根', () => {
    const value = formatNavSearchValue(pipeline)
    expect(value).toContain('/data-integration/pipelines')
    expect(parseNavSearchValue(value)).toEqual(pipeline)
  })
})

describe('scoreNavSearch · 拼音首字母按字段连续匹配', () => {
  it('sjgd 前缀命中数据管道，包含命中我的数据管道', () => {
    expect(scoreNavSearch(pipeline, 'sjgd')).toBe(NAV_SEARCH_SCORE.titleInitialPrefix)
    expect(scoreNavSearch(myPipeline, 'sjgd')).toBe(NAV_SEARCH_SCORE.titleInitialContains)
  })

  it('sjgdz 不跨字段、不借 href 英文字母', () => {
    expect(scoreNavSearch(pipeline, 'sjgdz')).toBe(0)
    expect(scoreNavSearch(myPipeline, 'sjgdz')).toBe(0)
    expect(scoreNavSearch(dict, 'sjgdz')).toBe(0)
    expect(scoreNavSearch(codeRule, 'sjgdz')).toBe(0)
    expect(scoreNavSearch(employee, 'sjgdz')).toBe(0)
    expect(scoreNavSearch(topic, 'sjgdz')).toBe(0)
  })

  it('sjzd / bmgu 只打对应菜单名', () => {
    expect(scoreNavSearch(dict, 'sjzd')).toBe(NAV_SEARCH_SCORE.titleInitialPrefix)
    expect(scoreNavSearch(codeRule, 'bmgz')).toBe(NAV_SEARCH_SCORE.titleInitialPrefix)
    expect(scoreNavSearch(pipeline, 'sjzd')).toBe(0)
  })

  it('sjjc 只作为分组命中，不抬到菜单名分数', () => {
    expect(scoreNavSearch(pipeline, 'sjjc')).toBe(NAV_SEARCH_SCORE.groupInitialPrefix)
    expect(scoreNavSearch(codeRule, 'sjjc')).toBe(0)
  })
})

describe('scoreNavSearch · 原文与全拼', () => {
  it('数据管道 / shujuguandao 命中菜单名', () => {
    expect(scoreNavSearch(pipeline, '数据管道')).toBe(NAV_SEARCH_SCORE.titleText)
    expect(scoreNavSearch(pipeline, 'shujuguandao')).toBe(NAV_SEARCH_SCORE.titleText)
  })

  it('空查询视为全通过', () => {
    expect(scoreNavSearch(pipeline, '')).toBe(1)
    expect(scoreNavSearch(pipeline, '   ')).toBe(1)
  })
})

describe('scoreNavSearch · href 仅路径片段', () => {
  it('完整片段或前缀可中，拼音字母不能从路径里东拼西凑', () => {
    expect(scoreNavSearch(pipeline, 'pipelines')).toBe(NAV_SEARCH_SCORE.hrefSegment)
    expect(scoreNavSearch(pipeline, 'pipeline')).toBe(NAV_SEARCH_SCORE.hrefSegment)
    expect(scoreNavSearch(dict, 'data-dictionaries')).toBe(NAV_SEARCH_SCORE.hrefSegment)
    expect(scoreNavSearch(dict, 'sjgdz')).toBe(0)
  })
})

describe('navCommandFilter', () => {
  it('吃 format 后的 value，分数与 scoreNavSearch 一致', () => {
    const value = formatNavSearchValue(pipeline)
    expect(navCommandFilter(value, 'sjgd')).toBe(NAV_SEARCH_SCORE.titleInitialPrefix)
    expect(navCommandFilter(value, 'sjgdz')).toBe(0)
  })
})

describe('当前导航回归 · 禁止跨字段借字母', () => {
  it('ypl 只打名称或分组首字母真含 ypl 的项', () => {
    const unexpected: string[] = []
    for (const group of navGroups) {
      for (const item of group.items) {
        const built = buildNavSearchDoc({ title: item.title, group: group.title, href: item.href })
        const hit = scoreNavSearch(built, 'ypl') > 0
        const legitimate = [item.title, group.title].some((text) => {
          const lower = text.toLowerCase()
          return lower.includes('ypl') || pinyinInitials(text).includes('ypl')
        })
        if (hit !== legitimate) unexpected.push(`${item.title} / ${group.title}`)
      }
    }
    expect(unexpected).toEqual([])
  })
})
