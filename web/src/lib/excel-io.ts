'use client'

/**
 * 下载需要在客户端检查响应并提取文件名的小型二进制/文本产物：经 proxy 拉 blob 触发浏览器保存。
 * 大批量服务端流式导出禁止走这里；应让浏览器下载器直接消费原生 GET 响应流。
 */
export async function downloadXlsx(url: string, fallbackName: string, init?: RequestInit): Promise<void> {
  const res = await fetch(url, init)
  if (!res.ok) throw new Error(await res.text())
  const blob = await res.blob()
  const cd = res.headers.get('content-disposition') ?? ''
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(cd)?.[1]
  const plain = /filename="?([^";]+)"?/i.exec(cd)?.[1]
  const name = encoded ? decodeURIComponent(encoded) : (plain ?? fallbackName)
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = name
  a.click()
  URL.revokeObjectURL(a.href)
}
