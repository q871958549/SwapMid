'use client'

import { signalSessionIdentityChange } from '@/lib/client-session-recovery'
import {
  SESSION_IDENTITY_VERSION_HEADER,
  SESSION_STATE_HEADER,
  SESSION_STATE_IDENTITY_CHANGED,
  SESSION_STATE_UNAVAILABLE,
  buildSessionExpiredPath,
  buildSessionUnavailablePath,
} from '@/lib/session-availability'

/**
 * 带上传进度的 `POST multipart` 内核。
 *
 * 🔴 **为什么不是 `fetch`**：`fetch` 没有上传进度事件——请求体发送期间它不产出任何信号，
 * 只能等整个响应回来。大文件上传下 UI 只能干转圈，用户无从判断是在传、卡住了还是已经失败
 * （2026-08-11 用户反馈「状态一直停留在盖章中」即此）。`XMLHttpRequest.upload.onprogress`
 * 是浏览器**唯一**能拿到已发送字节数的接口，故此处刻意保留 XHR。
 *
 * 🔴 **会话围栏分支必须在这里做，不能留给调用方**：SessionGuard 给 `window.fetch` 打的补丁
 * 对 XHR 不生效，401 / 409(identity-changed) / 503(unavailable) 三种响应都要各上传入口自己接。
 * 这套协议是全站统一的、且漏接一处就是「上传失败但说不清为什么」——所以收在共享层，
 * 新增上传入口只需调用本函数。已按此协议实现的另一处是 `scan/ota`（先于本函数存在，
 * 逻辑等价、暂未迁移；改那里时可一并收编）。
 *
 * @throws {SessionTakeoverError} 会话围栏已接管页面（正在跳转或刷新），调用方应静默退出。
 * @throws {Error} 其余失败；`message` 为后端响应原文，调用方用 `getApiErrorMessage` 解出中文。
 */
export function uploadWithProgress<T>(options: UploadWithProgressOptions): Promise<T> {
  const { url, body, identityVersion, onProgress } = options

  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', url)
    xhr.setRequestHeader(SESSION_IDENTITY_VERSION_HEADER, identityVersion)
    if (options.responseType === 'blob') {
      xhr.responseType = 'blob'
    }
    options.onOpen?.(xhr)

    xhr.upload.onprogress = (e) => {
      onProgress?.({
        phase: 'uploading',
        loaded: e.loaded,
        // lengthComputable 为 false 时 e.total 是 0，不能拿去算百分比（会得到 Infinity/NaN）
        total: e.lengthComputable ? e.total : 0,
        percent: e.lengthComputable && e.total > 0
          ? Math.min(99, Math.round((e.loaded / e.total) * 100))
          : null,
      })
    }

    // 字节已全部发出、正在等服务端处理。这一段对大文件可能长达数秒（解析 + 盖章 + 写回），
    // 期间没有任何网络事件——不切阶段的话进度条会停在 99% 一动不动，看着就像卡死。
    xhr.upload.onload = () => {
      onProgress?.({ phase: 'processing', loaded: 0, total: 0, percent: null })
    }

    xhr.onload = () => {
      const state = xhr.getResponseHeader(SESSION_STATE_HEADER)

      if (xhr.status === 409 && state === SESSION_STATE_IDENTITY_CHANGED) {
        // XHR 可能迟到，响应里的版本未必仍是权威值；只广播变化并让新文档重新探测。
        signalSessionIdentityChange()
        window.location.reload()
        reject(new SessionTakeoverError('identity-changed'))
        return
      }
      if (xhr.status === 503 && state === SESSION_STATE_UNAVAILABLE) {
        window.location.href = buildSessionUnavailablePath(
          window.location.pathname + window.location.search,
        )
        reject(new SessionTakeoverError('unavailable'))
        return
      }
      if (xhr.status === 401) {
        window.location.href = buildSessionExpiredPath(
          window.location.pathname + window.location.search,
          identityVersion,
        )
        reject(new SessionTakeoverError('expired'))
        return
      }

      if (xhr.status >= 200 && xhr.status < 300) {
        if (options.responseType === 'blob') {
          if (!(xhr.response instanceof Blob)) {
            reject(new Error(`HTTP ${xhr.status}`))
            return
          }
          resolve(xhr.response as T)
          return
        }
        try {
          resolve(JSON.parse(xhr.responseText) as T)
        } catch {
          reject(new Error(`HTTP ${xhr.status}`))
        }
        return
      }

      if (options.responseType === 'blob') {
        const body = xhr.response
        const readError = body instanceof Blob ? body.text() : Promise.resolve(xhr.responseText ?? '')
        void readError.then(text => {
          reject(new Error(isJsonBody(text) ? text : `HTTP ${xhr.status}`))
        })
        return
      }

      // 响应体是 JSON 才原样抛出（getApiErrorMessage 能解出 ABP 的 error.message）；
      // 代理/网关回的 HTML 错误页整篇塞进 UI 只会刷屏，降级成状态码。
      reject(new Error(isJsonBody(xhr.responseText) ? xhr.responseText : `HTTP ${xhr.status}`))
    }

    xhr.onerror = () => reject(new Error('NETWORK_ERROR'))
    xhr.ontimeout = () => reject(new Error('NETWORK_ERROR'))

    xhr.send(body)
  })
}

export interface UploadWithProgressOptions {
  /** 同源绝对路径，如 `/api/proxy/stamp-jobs/{id}/items`。 */
  url: string
  body: FormData
  /** 当前会话身份世代，来自 `useSessionIdentityVersion()`。 */
  identityVersion: string
  onProgress?: (progress: UploadProgress) => void
  /**
   * 默认解析 JSON。照片压缩返回的是 xlsx 字节，用 `blob`。
   * 失败时仍把 JSON 错误正文交给 `getApiErrorMessage`。
   */
  responseType?: 'json' | 'blob'
  /** 在 `send` 之前拿到 xhr（用于登记以便中止）。不要在此改动已设的会话头。 */
  onOpen?: (xhr: XMLHttpRequest) => void
}

/** 上传的两个阶段：字节在路上 / 字节已送达、服务端处理中。 */
export type UploadPhase = 'uploading' | 'processing'

export interface UploadProgress {
  phase: UploadPhase
  loaded: number
  /** 总字节数；不可知时为 0。 */
  total: number
  /**
   * 0–99 的整数；不可知或已进入服务端处理阶段时为 `null`。
   * ⚠️ 刻意封顶 99：100% 应当由「服务端确认完成」来置，字节发完只代表**本地**发完了。
   */
  percent: number | null
}

/** 会话围栏已接管页面（正在跳转或刷新）。调用方应静默退出，不要再弹错误。 */
export class SessionTakeoverError extends Error {
  constructor(public readonly kind: 'identity-changed' | 'unavailable' | 'expired') {
    super(`session takeover: ${kind}`)
    this.name = 'SessionTakeoverError'
  }
}

function isJsonBody(text: string): boolean {
  const trimmed = text.trimStart()
  return trimmed.startsWith('{') || trimmed.startsWith('[')
}
