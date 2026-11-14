import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { test } from 'node:test'
import { Linter } from 'eslint'
import { analyzeSource, compare, createLookup, LIMITS, numericTitlePatterns, resolveConsumerRoutes, textWidth, toBaseline, TREND_HEADER } from './ui-health.mjs'
import { countBulkSwapMidSuppressions, countContractRoutes, countInlineSwapMidDisables, mergeTrend } from './ui-health-trend.mjs'
import { checkUiRuleCardSync, globToRegExp, measureMatchedRules, PAGE_RULE_BUDGET_BYTES, parseRulePaths, REPO_ROOT } from './ui-rule-card.mjs'
import { findPaletteClasses } from '../eslint-rules/no-palette-classes.mjs'
import pageHeaderActions from '../eslint-rules/page-header-actions.mjs'
import singlePrimaryPerRegion from '../eslint-rules/single-primary-per-region.mjs'
import aboveTableBudget from '../eslint-rules/above-table-budget.mjs'

test('页头：数文字按钮与主按钮，「更多」里的菜单项不计', () => {
  const m = analyzeSource(`
    export function P() {
      return <PageHeader title="样品" action={<div>
        <Button size="sm">登记</Button>
        <Button size="sm" variant="outline">导出</Button>
        <IconButton label="刷新"><RefreshCw /></IconButton>
        <DropdownButton label="更多" items={<><Button>藏起来的</Button></>} />
      </div>} />
    }`)
  assert.equal(m.headerTextButtons, 3)
  assert.equal(m.headerPrimary, 1)
})

test('页头描述：按变量名找到命名空间后取简体文案量长度', () => {
  const lookup = createLookup({ Ns: { desc: '一'.repeat(41) }, Common: { desc: '短' } })
  const m = analyzeSource(`
    const t = useTranslations('Ns')
    const tc = useTranslations('Common')
    export const P = () => <><PageHeader title="x" description={t('desc')} /><PageHeader title="y" description={tc('desc')} /></>`, 'f.tsx', lookup)
  assert.equal(m.descriptionLength, 41)
})

test('textWidth：汉字计 1，其余计 0.5', () => {
  assert.equal(textWidth('导出Excel'), 4)
})

test('表上方常驻块：不计页头和弹层，对话框里的表格不算', () => {
  const m = analyzeSource(`
    export const P = () => <ListPageShell>
      <PageHeader title="x" />
      <FilterBar />
      <p>口径说明</p>
      <div className="rounded-lg border">汇总</div>
      <SettingsDialog />
      <DataTable columns={c} data={d} />
      <Dialog><DialogContent><p>说明</p><div>块</div><div>块</div><DataTable columns={c} data={d} /></DialogContent></Dialog>
    </ListPageShell>`)
  assert.equal(m.aboveTableBlocks, 3)
})

test('对话框：页脚主按钮数；默认钉底，改了内边距或 overflow 才算页脚随正文滚动', () => {
  const m = analyzeSource(`
    export const P = () => <>
      <DialogContent><DialogFooter><Button>预览</Button><Button>确认</Button></DialogFooter></DialogContent>
      <DialogContent className="sm:max-w-lg"><DialogFooter><Button>保存</Button></DialogFooter></DialogContent>
      <DialogContent className="flex max-h-[85vh] flex-col overflow-hidden p-0"><DialogFooter><Button>保存</Button></DialogFooter></DialogContent>
      <DialogContent className={cn('p-0', wide && 'sm:max-w-3xl')}><DialogFooter><Button>保存</Button></DialogFooter></DialogContent>
      <DialogContent className="overflow-visible"><DialogFooter><Button>保存</Button></DialogFooter></DialogContent>
    </>`)
  assert.equal(m.footerPrimary, 2)
  assert.equal(m.unpinnedDialogFooters, 2)
})

test('页头结构化插槽：主按钮、次按钮计入，「更多」算一个按钮', () => {
  const m = analyzeSource(`
    export const P = () => <PageHeader
      title="产值"
      help={{ label: '口径', content: '说明' }}
      primaryAction={<Button size="sm">导入</Button>}
      secondaryActions={[<Button key="a" variant="outline">导出</Button>, <Button key="b" variant="outline">刷新</Button>]}
      moreActions={<><DropdownMenuItem>覆盖情况</DropdownMenuItem><DropdownMenuItem>管理产线</DropdownMenuItem></>}
    />`)
  assert.equal(m.headerTextButtons, 4)
  assert.equal(m.headerPrimary, 1)
})

test('手写选中态：主按钮色与描边 / 幽灵来回切才计；写明理由的例外放行', () => {
  const m = analyzeSource(`
    export const P = () => <>
      <Button variant={a ? 'default' : 'outline'}>全部</Button>
      <Button variant={b ? 'secondary' : 'ghost'}>只看逾期</Button>
      <Button variant={ok ? 'default' : 'destructive'}>通过</Button>
      {/* ui-health-allow variantToggles: 预检后主次互换，不是选中态 */}
      <Button variant={p ? 'outline' : 'default'}>预检</Button>
      <Badge variant={on ? 'default' : 'outline'}>状态</Badge>
    </>`)
  assert.equal(m.variantToggles, 2)
})

test('实心红：只允许在确认页脚里', () => {
  const m = analyzeSource(`
    export const P = () => <>
      <Button variant="destructive">批量删除</Button>
      <Button variant={danger ? 'destructive' : 'outline'}>作废</Button>
      <Button variant="destructive-outline">批量删除</Button>
      <DialogFooter><Button variant="destructive">确认删除</Button></DialogFooter>
      <AlertDialogFooter><div><Button variant={x ? 'destructive' : 'default'}>确认</Button></div></AlertDialogFooter>
      <ScrollableDialogContent title="判定" footer={<div><Button variant="destructive">提交判定</Button></div>}>
        <Button variant="destructive">正文里的删除</Button>
      </ScrollableDialogContent>
      <Panel footer={<Button variant="destructive">不是弹层</Button>} />
    </>`)
  assert.equal(m.solidDestructive, 4)
})

test('超长按钮文案：超过 6 个汉字才计', () => {
  const m = analyzeSource(`export const P = () => <><Button>下载完整校验与差异报告</Button><Button>登记样品</Button></>`)
  assert.equal(m.longButtonLabels, 1)
})

test('任意像素值：带变体前缀的也计', () => {
  const m = analyzeSource(`export const P = () => <div className="w-[120px] md:max-h-[300px] w-full text-[13px]" />`)
  assert.equal(m.arbitraryPx, 3)
})

test('数字列：只靠标题推断才右对齐的列计入；工厂、meta.numeric、numberRange、看不见的 meta 不计', () => {
  const lookup = createLookup({ Ns: { qty: '出库数量', name: '名称' } })
  const m = analyzeSource(`
    const t = useTranslations('Ns')
    const columns = [
      { accessorKey: 'qty', header: ({ column }) => <DataTableColumnHeader column={column} title={t('qty')} /> },
      { accessorKey: 'rows', header: '行数' },
      { id: 'hours', accessorFn: (r) => r.h, meta: { title: '工时' }, header: () => null },
      { accessorKey: 'name', header: ({ column }) => <DataTableColumnHeader column={column} title={t('name')} /> },
      { accessorKey: 'amount', meta: { numeric: true }, header: '金额' },
      { accessorKey: 'files', meta: { numeric: false }, header: '文件数' },
      { accessorKey: 'age', meta: { filterVariant: 'numberRange' }, header: '账龄' },
      { accessorKey: 'cost', meta: sharedMeta, header: '成本' },
      { ...base, accessorKey: 'price', header: '单价' },
      moneyColumn('total', '总价'),
      { id: 'param', header: '参数' },
      { label: '数量', value: 1 },
    ]`, 'f.tsx', lookup)
  assert.equal(m.inferredNumericColumns, 3)
})

test('数字列判定读 numeric-column.ts 的正则；抽不到就报错', () => {
  const patterns = numericTitlePatterns(readFileSync(new URL('../src/components/data-table/numeric-column.ts', import.meta.url), 'utf8'))
  const numeric = (title) => patterns.some((re) => re.test(title))
  for (const title of ['行数', '金额', '实际作业工时', '毛利率', '毛重（kg）']) assert.ok(numeric(title), title)
  for (const title of ['参数', '质量', '名称', '执行频率', '在途']) assert.ok(!numeric(title), title)
  assert.throws(() => numericTitlePatterns('const OTHER = /x/'))
})

test('基线：只记超限项；只拦超过基线的部分', () => {
  const routes = {
    a: { ...Object.fromEntries(Object.keys(LIMITS).map((k) => [k, 0])), headerTextButtons: 5 },
    b: { ...Object.fromEntries(Object.keys(LIMITS).map((k) => [k, 0])), descriptionLength: 30 },
  }
  const baseline = toBaseline(routes)
  assert.deepEqual(baseline, { a: { headerTextButtons: 5 } })

  assert.deepEqual(compare(routes, baseline).regressions, [])
  const worse = { ...routes, a: { ...routes.a, headerTextButtons: 6 }, b: { ...routes.b, descriptionLength: 41 } }
  assert.deepEqual(compare(worse, baseline).regressions.map((r) => `${r.route}.${r.metric}`), ['a.headerTextButtons', 'b.descriptionLength'])
  const better = { ...routes, a: { ...routes.a, headerTextButtons: 4 } }
  assert.equal(compare(better, baseline).improvements.length, 1)
})

test('调色板类：彩色色阶命中，中性色与语义 token 放行', () => {
  assert.deepEqual(findPaletteClasses('bg-blue-500 dark:text-red-100 border-t-amber-300 hover:bg-emerald-50/40'), ['bg-blue-500', 'dark:text-red-100', 'border-t-amber-300', 'hover:bg-emerald-50'])
  assert.deepEqual(findPaletteClasses('bg-primary text-chart-2 bg-success/15 text-gray-500 bg-slate-100 text-destructive'), [])
})

test('趋势：swapmid/* 豁免数 = 批量抑制计数 + 行内 eslint-disable，理由里的规则名不计', () => {
  assert.equal(countBulkSwapMidSuppressions({
    'a.tsx': { 'swapmid/focus-ring-token': { count: 2 }, 'react-hooks/exhaustive-deps': { count: 5 } },
    'b.tsx': { 'swapmid/prefer-crud-form': { count: 1 } },
  }), 3)
  assert.equal(countInlineSwapMidDisables([
    '// eslint-disable-next-line swapmid/prefer-crud-form -- 与 swapmid/no-palette-classes 无关',
    '/* eslint-disable swapmid/focus-ring-token, swapmid/prefer-crud-form */',
    '// eslint-disable-next-line react-hooks/exhaustive-deps',
  ].join('\n')), 3)
})

test('趋势：契约路由按字面量去重，查询串不算新路由', () => {
  assert.equal(countContractRoutes([
    "{ path: '/finance/customs', role: 'customs' }, { path: '/finance/customs/reference-data?kind=9', role: 'customs' }",
    "{ path: '/finance/customs', ready: 'x' }; await page.screenshot({ path: file })",
  ]), 2)
})

test('趋势：追加一行；表头加列时旧行补空列；末行同一提交不重复写', () => {
  const header = 'date,commit,routes,extra'
  const first = mergeTrend('date,commit,routes\n2026-11-06,aaa,195\n', header, '2026-11-11,bbb,213,7')
  assert.equal(first.appended, true)
  assert.equal(first.text, 'date,commit,routes,extra\n2026-11-06,aaa,195,\n2026-11-11,bbb,213,7\n')
  const again = mergeTrend(first.text, header, '2026-11-12,bbb,213,7')
  assert.equal(again.appended, false)
  assert.equal(again.text, first.text)
  assert.equal(mergeTrend('', header, '2026-11-11,bbb,213,7').text, 'date,commit,routes,extra\n2026-11-11,bbb,213,7\n')
})

test('趋势：表头列数与仓库里的趋势文件一致', () => {
  const csv = readFileSync(new URL('../../docs/metrics/ui-health-trend.csv', import.meta.url), 'utf8').trim().split(/\r?\n/)
  assert.equal(csv[0], TREND_HEADER)
  for (const line of csv.slice(1)) assert.equal(line.split(',').length, TREND_HEADER.split(',').length, line)
})
test('规则卡与预算：三端硬规则卡同源一致，业务页规则注入不超过 20KB', () => {
  assert.deepEqual(parseRulePaths('---\npaths:\n  - "web/src/app/(dashboard)/**/*.tsx"\n---\n# 标题\n'), ['web/src/app/(dashboard)/**/*.tsx'])
  assert.equal(globToRegExp('web/src/app/(dashboard)/**/*.tsx').test('web/src/app/(dashboard)/sales/client.tsx'), true)
  assert.equal(globToRegExp('web/src/components/data-table/**').test('web/src/app/(dashboard)/sales/client.tsx'), false)
  assert.deepEqual(checkUiRuleCardSync(REPO_ROOT), [])
  const { totalBytes } = measureMatchedRules(REPO_ROOT)
  assert.ok(totalBytes > 0 && totalBytes <= PAGE_RULE_BUDGET_BYTES, `业务页规则注入 ${totalBytes} 字节，应 ≤ ${PAGE_RULE_BUDGET_BYTES}`)
})

test('互斥分支、纯图标引导按钮与窄屏替代块：不误计超限', () => {
  const m = analyzeSource(`
    export const P = ({ step, mode }) => <div className="flex min-h-0 flex-1 flex-col">
      <PageHeader
        title="客户"
        extra={<SalesGuideButton page="customers" />}
        secondaryActions={[<Button key="opt" variant="outline">选项</Button>, <Button key="imp" variant="outline">导入</Button>]}
        primaryAction={<CreateButton label="新建" onClick={open} />}
      />
      <FilterBar />
      {mode === 'a' ? <div>提示A</div> : <div>提示B</div>}
      <div className="flex flex-col md:hidden"><MobileList /></div>
      <div className="hidden md:flex"><DataTable columns={c} data={d} /></div>
      <Dialog><DialogContent>
        <DialogFooter>
          <Button variant="outline">取消</Button>
          {step === 1 ? <Button>下一步</Button> : <Button>提交</Button>}
        </DialogFooter>
      </DialogContent></Dialog>
    </div>`)
  assert.equal(m.headerTextButtons, 3)
  assert.equal(m.headerPrimary, 1)
  assert.equal(m.aboveTableBlocks, 2)
  assert.equal(m.footerPrimary, 1)
})

test('子模块路由归属：无 page.tsx 的共享面板按 import 图归入真实消费路由，不污染根路由', () => {
  const tempRoot = mkdtempSync(join(tmpdir(), 'swapmid-ui-health-'))
  try {
    const fakeApp = resolve(tempRoot, 'src/app')
    const sharedPanel = resolve(fakeApp, '(dashboard)/system/shared-panel/shared-list-client.tsx')
    const jobsPage = resolve(fakeApp, '(dashboard)/system/scheduled-jobs/page.tsx')
    const execPage = resolve(fakeApp, '(dashboard)/system/scheduled-executions/page.tsx')
    const sources = new Map([
      [sharedPanel, 'export function CaseListClient() { return null }'],
      [jobsPage, "import { CaseListClient } from '../shared-panel/shared-list-client'\nexport default function P() { return <CaseListClient /> }"],
      [execPage, "import { CaseListClient } from '../shared-panel/shared-list-client'\nexport default function P() { return <CaseListClient /> }"],
    ])
    for (const [file, content] of sources) {
      mkdirSync(dirname(file), { recursive: true })
      writeFileSync(file, content, 'utf8')
    }
    const { direct, consumersOf } = resolveConsumerRoutes([...sources.keys()], sources, fakeApp)
    assert.equal(direct.get(sharedPanel), null)
    assert.deepEqual(consumersOf(sharedPanel), [
      '(dashboard)/system/scheduled-executions',
      '(dashboard)/system/scheduled-jobs',
    ])
  } finally {
    rmSync(tempRoot, { recursive: true, force: true })
  }
})

function lintJsx(code) {
  const linter = new Linter({ configType: 'flat' })
  return linter.verify(code, [
    {
      files: ['**/*.jsx'],
      languageOptions: {
        ecmaVersion: 'latest',
        sourceType: 'module',
        parserOptions: { ecmaFeatures: { jsx: true } },
      },
      plugins: {
        swapmid: {
          rules: {
            ...pageHeaderActions.rules,
            ...singlePrimaryPerRegion.rules,
            ...aboveTableBudget.rules,
          },
        },
      },
      rules: {
        'swapmid/page-header-actions': 'error',
        'swapmid/single-primary-per-region': 'error',
        'swapmid/above-table-budget': 'error',
      },
    },
  ], 'page.jsx')
}

test('ESLint 护栏：swapmid/page-header-actions 与 swapmid/single-primary-per-region', () => {
  // 合规：GuideButton 纯图标 + 2 次按钮 + 1 主按钮；页脚互斥三元单主按钮
  assert.deepEqual(lintJsx(`
    export const Ok = ({ step }) => <>
      <PageHeader
        title="收款"
        extra={<SalesGuideButton page="receipts" />}
        secondaryActions={[<Button key="1" variant="outline">选项</Button>, <Button key="2" variant="outline">导入</Button>]}
        primaryAction={<CreateButton label="新建" />}
      />
      <DialogFooter>
        <Button variant="outline">取消</Button>
        {step === 0 ? <Button>下一步</Button> : <Button>提交</Button>}
      </DialogFooter>
    </>
  `), [])

  // 违规：页头 4 个文字按钮 + 2 个主按钮；页脚 2 个主按钮；DataTable bulkActions 2 个主按钮
  const errors = lintJsx(`
    export const Bad = () => <>
      <PageHeader
        title="超限"
        secondaryActions={[<Button key="1" variant="outline">A</Button>, <Button key="2" variant="outline">B</Button>]}
        moreActions={<DropdownMenuItem>C</DropdownMenuItem>}
        primaryAction={<><Button>主1</Button><CreateButton label="主2" /></>}
      />
      <DialogFooter><Button>存草稿</Button><Button>提交</Button></DialogFooter>
      <DataTable bulkActions={(rows) => <><Button>批量通过</Button><Button>批量归档</Button></>} />
    </>
  `)
  assert.deepEqual(
    errors.map((e) => `${e.ruleId}:${e.messageId}`),
    [
      'swapmid/page-header-actions:tooManyButtons',
      'swapmid/page-header-actions:multiplePrimary',
      'swapmid/single-primary-per-region:multiplePrimary',
      'swapmid/single-primary-per-region:multiplePrimary',
    ],
  )
})

test('ESLint 护栏：swapmid/above-table-budget 拦截表前超预算堆叠并放行 ListPageShell 插槽', () => {
  // 合规：ListPageShell 插槽 + 弹层内表格不误报
  assert.deepEqual(lintJsx(`
    export const Ok = () => (
      <ListPageShell
        header={<PageHeader title="报表" />}
        filters={<FilterBar />}
        notice={<p>状态提示</p>}
        summary={<ReportingSummaryPanel />}
      >
        <div className="md:hidden"><MobileCards /></div>
        <DataTable columns={c} data={d} />
        <Dialog><DialogContent><div>1</div><div>2</div><div>3</div><DataTable columns={c} data={d} /></DialogContent></Dialog>
      </ListPageShell>
    )
  `), [])

  // 违规：裸容器在 DataTable 上方堆叠 3 个常驻占流块
  const errors = lintJsx(`
    export const Bad = () => (
      <div className="flex min-h-0 flex-1 flex-col">
        <PageHeader title="报表" />
        <FilterBar />
        <p>长口径说明</p>
        <ReportingSummaryPanel />
        <DataTable columns={c} data={d} />
      </div>
    )
  `)
  assert.deepEqual(errors.map((e) => `${e.ruleId}:${e.messageId}`), ['swapmid/above-table-budget:overBudget'])
})
