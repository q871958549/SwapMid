import { describe, it } from 'node:test'
import assert from 'node:assert/strict'
import { mkdtempSync, rmSync, existsSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import nextTs from 'eslint-config-next/typescript'
import { Linter } from 'eslint'
import datatableListRoot from '../eslint-rules/datatable-list-root.mjs'
import preferCrudForm from '../eslint-rules/prefer-crud-form.mjs'
import iconButtonLabel from '../eslint-rules/icon-button-label.mjs'
import dialogMaxWidthPrefix from '../eslint-rules/dialog-max-width-prefix.mjs'
import noNativeDialogControls from '../eslint-rules/no-native-dialog-controls.mjs'
import noPaletteClasses from '../eslint-rules/no-palette-classes.mjs'
import focusRingToken from '../eslint-rules/focus-ring-token.mjs'
import pageHeaderActions from '../eslint-rules/page-header-actions.mjs'
import singlePrimaryPerRegion from '../eslint-rules/single-primary-per-region.mjs'
import aboveTableBudget from '../eslint-rules/above-table-budget.mjs'
import { analyzeSource, LIMITS } from './ui-health.mjs'
import { SUPPORTED_KINDS, generatePageFiles, parseCliArgs, scaffoldPage } from './new-page.mjs'

const hanPlugin = {
  rules: {
    ...datatableListRoot.rules,
    ...preferCrudForm.rules,
    ...iconButtonLabel.rules,
    ...dialogMaxWidthPrefix.rules,
    ...noNativeDialogControls.rules,
    ...noPaletteClasses.rules,
    ...focusRingToken.rules,
    ...pageHeaderActions.rules,
    ...singlePrimaryPerRegion.rules,
    ...aboveTableBudget.rules,
  },
}

const hanRulesConfig = Object.fromEntries(
  Object.keys(hanPlugin.rules).map((name) => [`swapmid/${name}`, 'error']),
)

function verifyWithAllHanRules(code, filename) {
  const linter = new Linter({ configType: 'flat' })
  return linter.verify(
    code,
    [
      ...nextTs,
      {
        files: ['**/*.tsx'],
        plugins: { swapmid: hanPlugin },
        rules: hanRulesConfig,
      },
    ],
    { filename },
  )
}

describe('new-page.mjs 新页面脚手架', () => {
  it('全部六种模板（list/crud/report/form/detail/dashboard）均零 ui-health 违规且通过全套 swapmid/* ESLint 规则', () => {
    for (const kind of SUPPORTED_KINDS) {
      const { files } = generatePageFiles(`sales-management/demo-${kind}`, kind)
      for (const [name, source] of Object.entries(files)) {
        const metrics = analyzeSource(source, name)
        for (const [metric, limit] of Object.entries(LIMITS)) {
          assert.ok(
            metrics[metric] <= limit,
            `${kind}/${name} 指标 ${metric}=${metrics[metric]} 超过规范上限 ${limit}`,
          )
        }
        assert.equal(metrics.arbitraryPx, 0, `${kind}/${name} 不得含任意像素类`)
        const lintErrors = verifyWithAllHanRules(source, `src/app/(dashboard)/sales-management/demo-${kind}/${name}`)
        assert.deepEqual(lintErrors, [], `${kind}/${name} 触发 swapmid/* 报错：${JSON.stringify(lintErrors)}`)
      }
    }
  })

  it('校验路由合法性、支持 --dry-run 并在目标已存在时拒绝覆盖', () => {
    assert.throws(() => parseCliArgs(['../escape']), /非法路由段|路由路径须为/)
    assert.throws(() => parseCliArgs(['single-segment']), /路由路径须为/)
    assert.throws(() => parseCliArgs(['sales/orders', '--kind', 'unknown']), /不支持的页面类型/)

    const tempRoot = mkdtempSync(join(tmpdir(), 'swapmid-new-page-'))
    try {
      const dry = scaffoldPage({ slug: 'sales-management/rebate-ledger', kind: 'crud', dryRun: true, dashboardDir: tempRoot })
      assert.equal(existsSync(dry.targetDir), false)

      const written = scaffoldPage({ slug: 'sales-management/rebate-ledger', kind: 'crud', dryRun: false, dashboardDir: tempRoot })
      assert.equal(existsSync(join(written.targetDir, 'page.tsx')), true)
      assert.equal(existsSync(join(written.targetDir, 'client.tsx')), true)

      assert.throws(
        () => scaffoldPage({ slug: 'sales-management/rebate-ledger', kind: 'crud', dryRun: false, dashboardDir: tempRoot }),
        /拒绝覆盖/,
      )
    } finally {
      rmSync(tempRoot, { recursive: true, force: true })
    }
  })
})
