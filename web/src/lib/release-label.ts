/**
 * 界面展示的发布标识。
 *
 * 不要用 package.json / 写死的 v0.1.0：那些是脚手架残留，不会随发布变。
 * 生产由服务端读取运行时环境变量 `SWAPMID_RELEASE`（compose 注入 IMAGE_TAG，
 * `YYYYMMDD-HHMMSS-sha12`；UAT 为 `git-<sha12>`），再把原文字符串传进来。
 * 本函数不读 process.env，也不接受 NEXT_PUBLIC_*：Next 会把后者打进客户端包，
 * 每次换发布标识都会让 `pnpm build` 层失效。未传入时显示 `dev`。
 */
export function getReleaseDisplay(raw?: string | null): {
  text: string
  /** 底栏常显：生产只露日期，避免和重置按钮挤在一起。完整标识在 text / title。 */
  shortText: string
  title: string
} {
  const tag = raw?.trim() ?? ''
  if (!tag) {
    return { text: 'dev', shortText: 'dev', title: 'local development' }
  }

  const prod = /^(\d{8})-\d{6}-([0-9a-f]{7,})$/i.exec(tag)
  if (prod) {
    return { text: `${prod[1]} · ${prod[2].slice(0, 8)}`, shortText: prod[1], title: tag }
  }

  const uat = /^git-([0-9a-f]{7,})$/i.exec(tag)
  if (uat) {
    const short = uat[1].slice(0, 8)
    return { text: short, shortText: short, title: tag }
  }

  return { text: tag, shortText: tag, title: tag }
}
