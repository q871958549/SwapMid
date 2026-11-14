'use client'

/* eslint-disable @next/next/no-img-element -- 知识库 Markdown 正文中的图片为动态 src，不适用 next/image */

import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

/**
 * 知识中心 Markdown 渲染器：react-markdown + remark-gfm（表格 / 任务列表 / 删除线）。
 * 用 Tailwind 原子类逐元素映射（不依赖 @tailwindcss/typography 插件，平台未装）。
 * ⚠️ Mermaid 代码块（```mermaid）MVP 暂作普通代码块原样显示，P1 再接 mermaid.js 渲染。
 * 🔒 XSS：未启用 rehype-raw，源文中的原始 HTML 被当文本转义；a/img 的 url 由 react-markdown
 *    内置 defaultUrlTransform 净化（剥离 javascript:/data: 等危险协议）。**勿传自定义 urlTransform
 *    或加 rehype-raw**，否则会失去这层默认防护。
 */
export function MarkdownRenderer({ content }: { content: string }) {
  return (
    <div className="text-sm leading-relaxed text-foreground">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          // 字号层级：页面文章标题已是 text-3xl，正文标题整体降一档、逐级拉开，避免与页面标题撞大小。
          h1: ({ children }) => <h1 className="mt-8 mb-4 border-b pb-2 text-xl font-bold">{children}</h1>,
          h2: ({ children }) => <h2 className="mt-7 mb-3 border-b pb-1.5 text-lg font-bold">{children}</h2>,
          h3: ({ children }) => <h3 className="mt-5 mb-2 text-[0.95rem] font-semibold">{children}</h3>,
          h4: ({ children }) => <h4 className="mt-4 mb-1.5 text-sm font-semibold text-foreground/90">{children}</h4>,
          p: ({ children }) => <p className="my-3">{children}</p>,
          ul: ({ children }) => <ul className="my-3 ml-6 list-disc space-y-1">{children}</ul>,
          ol: ({ children }) => <ol className="my-3 ml-6 list-decimal space-y-1">{children}</ol>,
          li: ({ children }) => <li className="pl-1">{children}</li>,
          blockquote: ({ children }) => (
            <blockquote className="my-3 border-l-4 border-primary/40 bg-muted/40 py-1 pl-4 text-muted-foreground">{children}</blockquote>
          ),
          a: ({ href, children }) => (
            <a href={href} className="text-primary underline underline-offset-2 hover:opacity-80" target="_blank" rel="noopener noreferrer">{children}</a>
          ),
          code: ({ className, children }) => {
            // 块级代码（```lang）带 language- className，由 <pre> 控制外观；行内 code 自带底色。
            if (className?.includes('language-')) {
              return <code className={className}>{children}</code>
            }
            return <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-[0.85em]">{children}</code>
          },
          pre: ({ children }) => (
            <pre className="my-4 overflow-x-auto rounded-lg bg-muted p-4 font-mono text-[0.8em] leading-relaxed">{children}</pre>
          ),
          table: ({ children }) => (
            <div className="my-4 overflow-x-auto"><table className="w-full border-collapse text-sm">{children}</table></div>
          ),
          thead: ({ children }) => <thead className="border-b bg-muted/50">{children}</thead>,
          th: ({ children }) => <th className="border px-3 py-2 text-left font-semibold">{children}</th>,
          td: ({ children }) => <td className="border px-3 py-2 align-top">{children}</td>,
          hr: () => <hr className="my-6 border-border" />,
          img: ({ src, alt }) => <img src={typeof src === 'string' ? src : undefined} alt={alt ?? ''} className="my-4 max-w-full rounded-lg border" />,
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  )
}
