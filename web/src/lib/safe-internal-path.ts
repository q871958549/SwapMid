const INTERNAL_BASE = 'https://internal.invalid'
const ASCII_CONTROL_OR_BACKSLASH = /[\u0000-\u001f\u007f\\]/

/**
 * 只允许当前站点的绝对路径。除显式拦截 // 外，还拒绝 WHATWG URL 会当作
 * 路径分隔符的反斜杠与会被解析器静默剥离的控制字符。
 */
export function isSafeInternalPath(value: string | null | undefined): value is string {
  if (!value
    || !value.startsWith('/')
    || value.startsWith('//')
    || ASCII_CONTROL_OR_BACKSLASH.test(value)) {
    return false
  }

  try {
    return new URL(value, INTERNAL_BASE).origin === INTERNAL_BASE
  } catch {
    return false
  }
}
