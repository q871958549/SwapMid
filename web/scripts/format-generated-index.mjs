// openapi-ts 把 src/lib/api/generated/index.ts 写成一整行 `export type { … } from './types.gen';`（400 KB+），
// 任意两个都改了 API 的分支必在这一行冲突——2026-11 回放近 30 天 82 次合并：18 次有冲突，16 次卷入此文件。
// 本脚本在 gen:api 之后把它改成「一行一个名字」，git 即可按行三方合并，同口径回放 16 → 1。
// 纯格式化：导出名集合与顺序不变；幂等（已是多行的不再改）。
import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const SINGLE_LINE_EXPORT = /^export (type )?\{ ([^}\n]+) \} from '([^'\n]+)';$/gm

export function formatGeneratedIndex(text) {
  return text.replace(SINGLE_LINE_EXPORT, (_, typeKw = '', names, from) =>
    `export ${typeKw}{\n${names.split(', ').map((n) => `  ${n},`).join('\n')}\n} from '${from}';`)
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const file = resolve(dirname(fileURLToPath(import.meta.url)), '..', process.argv[2] ?? 'src/lib/api/generated/index.ts')
  const before = readFileSync(file, 'utf8')
  const after = formatGeneratedIndex(before)
  if (after !== before) writeFileSync(file, after, 'utf8')
  console.log(`[gen:api] ${after === before ? '无需格式化' : '已拆为一行一个导出名'} → ${file}`)
}
