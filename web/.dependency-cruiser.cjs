/** @type {import('dependency-cruiser').IConfiguration} */
module.exports = {
  forbidden: [
    {
      name: 'no-runtime-circular-dependencies',
      severity: 'error',
      comment:
        '运行时循环依赖会在 Turbopack 生产模块求值期触发 TDZ；共享值应抽到不反向依赖业务文件的 shared.ts。',
      from: {},
      to: {
        circular: true,
        viaOnly: {
          dependencyTypesNot: ['type-only'],
        },
      },
    },
    {
      name: 'no-unresolvable-imports',
      severity: 'error',
      comment: '所有本地与 npm import 都必须能按 tsconfig/package.json 解析。',
      from: {},
      to: {
        couldNotResolve: true,
      },
    },
  ],
  options: {
    doNotFollow: {
      path: ['node_modules'],
    },
    moduleSystems: ['es6', 'cjs'],
    tsPreCompilationDeps: 'specify',
    tsConfig: {
      fileName: 'tsconfig.json',
    },
    enhancedResolveOptions: {
      exportsFields: ['exports'],
      conditionNames: ['import', 'require', 'node', 'default', 'types'],
      extensions: ['.js', '.jsx', '.mjs', '.cjs', '.ts', '.tsx', '.mts', '.cts', '.d.ts'],
    },
    skipAnalysisNotInRules: true,
  },
}
