import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MarkdownRenderer } from './markdown-renderer'

// 验证知识中心「渲染」环节：react-markdown + remark-gfm + 自定义 Tailwind 元素映射，
// 以及 security-reviewer 关注的 XSS 防护（未启用 rehype-raw / 默认 URL 净化）。
// 用例取自排产手册真实片段（标题 / GFM 表格 / 代码块 / 列表 / 引用）。
describe('MarkdownRenderer', () => {
  it('渲染标题、段落与无序列表', () => {
    render(<MarkdownRenderer content={'# 排产模块使用手册\n\n面向计划/生产人员。\n\n- 标准工时\n- 导入计划\n- 看板'} />)
    expect(screen.getByRole('heading', { level: 1, name: '排产模块使用手册' })).toBeInTheDocument()
    expect(screen.getByText('面向计划/生产人员。')).toBeInTheDocument()
    expect(screen.getByText('标准工时')).toBeInTheDocument()
    expect(screen.getByText('看板')).toBeInTheDocument()
  })

  it('渲染 GFM 表格（依赖 remark-gfm）', () => {
    render(<MarkdownRenderer content={'| 因子 | 含义 |\n|---|---|\n| UPPH | 每人每小时产量 |\n| 标准人数 | 整线标配人数 |'} />)
    expect(screen.getByRole('table')).toBeInTheDocument()
    expect(screen.getByText('因子')).toBeInTheDocument()
    expect(screen.getByText('每人每小时产量')).toBeInTheDocument()
    expect(screen.getByText('整线标配人数')).toBeInTheDocument()
  })

  it('渲染围栏代码块为 pre', () => {
    const { container } = render(<MarkdownRenderer content={'```\n日产能 = UPPH × 标准人数 × 日工时\n```'} />)
    expect(container.querySelector('pre')).not.toBeNull()
    expect(screen.getByText(/日产能/)).toBeInTheDocument()
  })

  it('渲染引用块与外链（target=_blank + rel）', () => {
    const { container } = render(<MarkdownRenderer content={'> 这是最常见的疑问。\n\n[开发文档](https://example.com/blueprint)'} />)
    expect(container.querySelector('blockquote')).not.toBeNull()
    expect(screen.getByText('这是最常见的疑问。')).toBeInTheDocument()
    const link = screen.getByRole('link', { name: '开发文档' })
    expect(link).toHaveAttribute('href', 'https://example.com/blueprint')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })

  it('XSS：原始 HTML 不被渲染为真实元素（未启用 rehype-raw）', () => {
    const { container } = render(
      <MarkdownRenderer content={'正常文本 <script>alert(1)</script> 以及 <img src=x onerror="alert(1)"> 结尾'} />,
    )
    // <script> 不应作为真实 DOM 元素注入
    expect(container.querySelector('script')).toBeNull()
    // 即便出现 img，也不得带 onerror 事件属性
    container.querySelectorAll('img').forEach((img) => {
      expect(img.getAttribute('onerror')).toBeNull()
    })
  })

  it('XSS：javascript: 协议链接被默认 urlTransform 净化', () => {
    const { container } = render(<MarkdownRenderer content={'[危险链接](javascript:alert(document.cookie))'} />)
    const href = container.querySelector('a')?.getAttribute('href') ?? ''
    expect(href).not.toContain('javascript:')
  })
})
