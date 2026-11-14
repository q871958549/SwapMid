import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render as rtlRender, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from './data-table'
import { DataTableColumnHeader } from './data-table-column-header'
import { SessionIdentityProvider } from '@/components/providers/session-identity-provider'
import zhHansMessages from '../../../messages/zh-Hans.json'

// DataTable 内部（工具栏/分页器/表头排序菜单/筛选行）经 useTranslations('Common') 求各类 chrome
// 文案（搜索/导入/导出/共 X 条/自定义每页条数等，i18n 第二期批次2b）——组件树需要
// NextIntlClientProvider 才能渲染。真实 zh-Hans messages 直供，保持与生产一致。
function render(ui: React.ReactElement) {
  return rtlRender(<NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>{ui}</NextIntlClientProvider>)
}

function renderWithIdentity(ui: React.ReactElement) {
  return rtlRender(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <SessionIdentityProvider
        identityVersion="audit-table-identity"
        storagePrincipalId="00000000-0000-0000-0000-000000000099"
      >
        {ui}
      </SessionIdentityProvider>
    </NextIntlClientProvider>,
  )
}

// DataTable 用 usePathname 推导资源权限：测试路径不在 nav-config 中 →
// resourcePermission=undefined → 权限门不拦（useHasPermission(undefined)=true），导出按钮直接显示。
// 导入按钮则另有一层：显隐完全由 onImportRows 是否传入决定（见「导入按钮由 onImportRows 推导」用例）。
vi.mock('next/navigation', () => ({ usePathname: () => '/test-table' }))

interface Row {
  id: string
  name: string
  status: string
  city: string
}

function col(
  accessorKey: keyof Row,
  title: string,
  extra: Partial<ColumnDef<Row, unknown>> = {},
): ColumnDef<Row, unknown> {
  return {
    accessorKey,
    header: ({ column }) => <DataTableColumnHeader column={column} title={title} />,
    ...extra,
  }
}

const columns: ColumnDef<Row, unknown>[] = [
  col('name', '姓名'),
  col('status', '状态', {
    cell: ({ row }) => (row.original.status === 'active' ? '启用' : '停用'),
    meta: {
      filterVariant: 'select',
      filterOptions: [
        { value: 'active', label: '启用' },
        { value: 'inactive', label: '停用' },
      ],
    },
  }),
  col('city', '城市'),
]

// 5 行、单页：搜索 / 筛选 / 显隐 / 选择 / compact 用。
const baseRows: Row[] = [
  { id: '1', name: '田烨', status: 'active', city: '北京' },
  { id: '2', name: '张三', status: 'inactive', city: '上海' },
  { id: '3', name: '李四', status: 'active', city: '北京' },
  { id: '4', name: '王五', status: 'inactive', city: '广州' },
  { id: '5', name: '赵六', status: 'active', city: '深圳' },
]

// 7 行：配 pageSize=3 测客户端分页切片 / 翻页。
const manyRows: Row[] = Array.from({ length: 7 }, (_, i) => ({
  id: String(i + 1),
  name: `员工${i + 1}`,
  status: i % 2 ? 'inactive' : 'active',
  city: `城市${i + 1}`,
}))

type Props = Partial<Parameters<typeof DataTable<Row>>[0]>

// 默认数据不含 creator/lastModifier/creationTime 等审计字段 → 不触发审计列 / loadAuditUsers 请求；
// 仍兜底桩一个 fetch，杜绝任何意外网络调用污染用例。
function stubEmptyFetch() {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [] }) } as Response))
}

beforeEach(() => {
  localStorage.clear()
  stubEmptyFetch()
  window.history.replaceState({}, '', '/test-table')
})

// render 后表格先渲染骨架屏，挂载 effect 置 hydrated 才出真实工具栏；await 搜索框出现以确保已 hydrated。
async function renderTable(props: Props = {}) {
  const view = render(<DataTable<Row> columns={columns} data={baseRows} tableKey="test" {...props} />)
  await screen.findByPlaceholderText(typeof props.searchPlaceholder === 'string' ? props.searchPlaceholder : '搜索...')
  return view
}

// 分页区 4 个 size-7 图标按钮：[0]首页 [1]上一页 [2]下一页 [3]末页。
// 「自定义每页条数」的 + 按钮同为 size-7，按 aria-label 排除，保持索引语义。
function pagerButtons(container: HTMLElement): HTMLButtonElement[] {
  return (Array.from(container.querySelectorAll('button.size-7')) as HTMLButtonElement[])
    .filter((b) => !b.getAttribute('aria-label')?.startsWith('自定义每页条数'))
}

// 表格外壳 div：compact 时 shrink-0、否则 flex-1。
function tableShell(container: HTMLElement): HTMLElement {
  return container.querySelector('div.rounded-xl.border') as HTMLElement
}

// 分页区（唯一带 justify-between 的块）的纯文本，用于断言「共 X 条 / 第 a / b 页」。
function pagerText(container: HTMLElement): string {
  return container.querySelector('.justify-between')?.textContent ?? ''
}

function bodyRowCount(container: HTMLElement): number {
  return container.querySelectorAll('tbody tr').length
}

// Radix DropdownMenu 的 Trigger 靠 onPointerDown 打开，jsdom 下 fireEvent.click 不触发；用键盘 Enter 打开。
// 「列」按钮打开列设置面板（Popover），面板里每列一个复选框。
function openColumnSettings() {
  fireEvent.click(screen.getByRole('button', { name: '列' }))
}

describe('DataTable · 渲染基础', () => {
  it('hydrated 后渲染搜索框、表头与数据行', async () => {
    await renderTable()
    expect(screen.getByPlaceholderText('搜索...')).toBeInTheDocument()
    expect(screen.getByText('姓名')).toBeInTheDocument()
    expect(screen.getByText('城市')).toBeInTheDocument()
    expect(screen.getByText('田烨')).toBeInTheDocument()
    expect(screen.getByText('赵六')).toBeInTheDocument()
  })

  it('支持自定义 searchPlaceholder', async () => {
    await renderTable({ searchPlaceholder: '搜索员工...' })
    expect(screen.getByPlaceholderText('搜索员工...')).toBeInTheDocument()
  })

  it('空数据渲染「暂无数据」', async () => {
    await renderTable({ data: [] })
    expect(screen.getByText('暂无数据')).toBeInTheDocument()
  })

  it('enableSearch=false 隐藏全局搜索框', async () => {
    // 不走 renderTable（其靠搜索框判断 hydrated）；用表头出现作 hydrated 锚点。
    render(<DataTable<Row> columns={columns} data={baseRows} tableKey="no-search" enableSearch={false} />)
    await screen.findByText('姓名')
    expect(screen.queryByPlaceholderText('搜索...')).not.toBeInTheDocument()
    // 列显隐等其余工具仍在
    expect(screen.getByRole('button', { name: '列' })).toBeInTheDocument()
  })
})

// 导入按钮的显隐完全由 onImportRows 推导，没有 enableImport 开关——「有实现才有按钮」。
// 历史：enableImport 默认 true 时，98 个用 DataTable 的页面里 82 个挂着导入按钮，而全站
// onImportRows 实现数为 0，点下去只弹一句「读取到 N 行」的 alert 就没了（2026-07-22 清理）。
// 本组用例把该约束焊死：谁把默认值改回「无脑显示」，这里立刻红。
describe('DataTable · 导入按钮由 onImportRows 推导', () => {
  it('未传 onImportRows：不渲染「导入」按钮', async () => {
    await renderTable()
    expect(screen.queryByRole('button', { name: '导入' })).not.toBeInTheDocument()
    // 同时断言工具栏其余按钮在场，排除「整条工具栏没渲染出来」造成的假阳性
    expect(screen.getByRole('button', { name: '导出' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '列' })).toBeInTheDocument()
  })

  it('传入 onImportRows：渲染「导入」按钮', async () => {
    await renderTable({ onImportRows: vi.fn() })
    expect(screen.getByRole('button', { name: '导入' })).toBeInTheDocument()
  })
})

describe('DataTable · 客户端分页', () => {
  it('按 pageSize 切片：7 行 / 每页 3 → 首页只显示前 3 行', async () => {
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    expect(screen.getByText('员工1')).toBeInTheDocument()
    expect(screen.getByText('员工3')).toBeInTheDocument()
    expect(screen.queryByText('员工4')).not.toBeInTheDocument()
    expect(bodyRowCount(container)).toBe(3)
    expect(pagerText(container)).toContain('共 7 条')
    expect(pagerText(container)).toContain('第 1 / 3 页')
  })

  it('点「下一页」翻到第 2 页', async () => {
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    fireEvent.click(pagerButtons(container)[2])
    expect(screen.getByText('员工4')).toBeInTheDocument()
    expect(screen.queryByText('员工1')).not.toBeInTheDocument()
    expect(pagerText(container)).toContain('第 2 / 3 页')
  })

  it('点「末页 / 首页」跳转，首末页按钮按位置禁用', async () => {
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    const first = () => pagerButtons(container)[0]
    const last = () => pagerButtons(container)[3]
    // 首页时：首页/上一页 禁用
    expect(first()).toBeDisabled()
    fireEvent.click(last())
    expect(screen.getByText('员工7')).toBeInTheDocument()
    expect(pagerText(container)).toContain('第 3 / 3 页')
    // 末页时：下一页/末页 禁用
    expect(last()).toBeDisabled()
    fireEvent.click(first())
    expect(pagerText(container)).toContain('第 1 / 3 页')
  })

  it('挂载后把每页条数持久化到 localStorage', async () => {
    await renderTable({ data: manyRows, pageSize: 3, tableKey: 'persist-size' })
    await waitFor(() => expect(localStorage.getItem('swapmid-table:persist-size:pageSize')).toBe('3'))
  })
})

describe('DataTable · 自定义每页条数', () => {
  function openCustomPanel(container: HTMLElement) {
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数' }))
  }

  it('添加自定义条数：持久化为本表选项并立即生效', async () => {
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    openCustomPanel(container)
    fireEvent.change(screen.getByPlaceholderText('如 10000'), { target: { value: '200' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:test:page-size-options') ?? '[]')).toContain(200),
    )
    expect(localStorage.getItem('swapmid-table:page-size-options')).toBeNull()
    expect(pagerText(container)).toContain('第 1 / 1 页')
  })

  it('无本表档位时回退读取全局旧键', async () => {
    localStorage.setItem('swapmid-table:page-size-options', JSON.stringify([200]))
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    openCustomPanel(container)
    expect(screen.getByRole('button', { name: '移除 200' })).toBeInTheDocument()
  })

  it('拒绝超过上限 10000 的输入', async () => {
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    openCustomPanel(container)
    fireEvent.change(screen.getByPlaceholderText('如 10000'), { target: { value: '20000' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    expect(screen.getByText(/不能超过 10000/)).toBeInTheDocument()
    expect(localStorage.getItem('swapmid-table:test:page-size-options')).toBeNull()
  })

  it('已添加的自定义条数可移除', async () => {
    localStorage.setItem('swapmid-table:test:page-size-options', JSON.stringify([200]))
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    openCustomPanel(container)
    fireEvent.click(screen.getByRole('button', { name: '移除 200' }))
    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:test:page-size-options') ?? '[]')).not.toContain(200),
    )
  })
})

describe('DataTable · 行虚拟化', () => {
  // 60 行阈值以上才虚拟化；造 200 行触发。
  const bigRows: Row[] = Array.from({ length: 200 }, (_, i) => ({
    id: String(i + 1),
    name: `员工${i + 1}`,
    status: i % 2 ? 'inactive' : 'active',
    city: `城市${i + 1}`,
  }))

  // jsdom 无布局：给所有元素一个固定高度，让 useVirtualizer 能算出可见区间（否则视口高 0、
  // 无法验证「只渲染少量行」）。用例内局部 mock，afterEach 还原，避免污染其它用例。
  let rectSpy: ReturnType<typeof vi.spyOn>
  beforeEach(() => {
    rectSpy = vi.spyOn(Element.prototype, 'getBoundingClientRect').mockReturnValue({
      width: 800, height: 400, top: 0, left: 0, right: 800, bottom: 400, x: 0, y: 0, toJSON: () => ({}),
    } as DOMRect)
  })
  afterEach(() => { rectSpy.mockRestore() })

  it('当页超 60 行：虚拟化生效，tbody 不全量渲染 200 行', async () => {
    const { container } = await renderTable({ data: bigRows, pageSize: 200 })
    // 组件正常：数据模型含 200 行（分页「共 200 条」）
    expect(pagerText(container)).toContain('共 200 条')
    // 未全量渲染——虚拟化没生效就会渲染满 200 行，此断言即失败（回归哨兵）。
    // jsdom 无真实视口高度，可见行数本身不稳定，故只断言「远少于全量」。
    expect(bodyRowCount(container)).toBeLessThan(200)
  })

  it('当页 ≤ 60 行：不虚拟化，全量渲染、行不带 data-index', async () => {
    const { container } = await renderTable({ data: manyRows, pageSize: 15 })
    expect(container.querySelectorAll('tbody tr[data-index]')).toHaveLength(0)
    expect(bodyRowCount(container)).toBe(7)
  })
})

describe('DataTable · 全局搜索', () => {
  it('输入关键字按 accessor 值过滤', async () => {
    const { container } = await renderTable()
    fireEvent.change(screen.getByPlaceholderText('搜索...'), { target: { value: '张三' } })
    expect(screen.getByText('张三')).toBeInTheDocument()
    expect(screen.queryByText('田烨')).not.toBeInTheDocument()
    expect(bodyRowCount(container)).toBe(1)
  })

  it('支持拼音首字母匹配（输入 ty 命中 田烨）', async () => {
    await renderTable()
    fireEvent.change(screen.getByPlaceholderText('搜索...'), { target: { value: 'ty' } })
    expect(screen.getByText('田烨')).toBeInTheDocument()
    expect(screen.queryByText('张三')).not.toBeInTheDocument()
  })

  it('过滤后「共 X 条」标注（过滤自 Y 条）', async () => {
    const { container } = await renderTable()
    fireEvent.change(screen.getByPlaceholderText('搜索...'), { target: { value: '张三' } })
    expect(pagerText(container)).toContain('共 1 条')
    expect(pagerText(container)).toContain('过滤自 5 条')
  })

  it('无匹配时显示「没有匹配的结果」', async () => {
    await renderTable()
    fireEvent.change(screen.getByPlaceholderText('搜索...'), { target: { value: '查无此人' } })
    expect(screen.getByText('没有匹配的结果')).toBeInTheDocument()
  })
})

describe('DataTable · 列筛选（客户端）', () => {
  it('点「筛选」展开筛选行、再点收起并清空', async () => {
    await renderTable()
    // 初始无筛选输入框
    expect(screen.queryByPlaceholderText('筛选...')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /筛选/ }))
    expect(screen.getAllByPlaceholderText('筛选...').length).toBeGreaterThan(0)
    // 文本列输入过滤
    fireEvent.change(screen.getAllByPlaceholderText('筛选...')[0], { target: { value: '田' } })
    expect(screen.queryByText('张三')).not.toBeInTheDocument()
    // 再点收起 → 筛选行消失且数据恢复（激活后按钮名含徽标数字，用正则匹配；
    // 锚在开头：有筛选时表头还有「已按此列筛选…」的漏斗按钮）
    fireEvent.click(screen.getByRole('button', { name: /^筛选/ }))
    expect(screen.queryByPlaceholderText('筛选...')).not.toBeInTheDocument()
    expect(screen.getByText('张三')).toBeInTheDocument()
  })

  it('文本列模糊过滤：输入「北」命中城市=北京的两行', async () => {
    const { container } = await renderTable()
    fireEvent.click(screen.getByRole('button', { name: /筛选/ }))
    // name / city 是文本筛选（status 为下拉），[1] 为城市列输入框
    const inputs = screen.getAllByPlaceholderText('筛选...')
    fireEvent.change(inputs[1], { target: { value: '北' } })
    expect(bodyRowCount(container)).toBe(2)
    expect(screen.getByText('田烨')).toBeInTheDocument()
    expect(screen.getByText('李四')).toBeInTheDocument()
    expect(screen.queryByText('张三')).not.toBeInTheDocument()
  })

  it('激活筛选后筛选按钮显示数量徽标', async () => {
    await renderTable()
    const filterBtn = screen.getByRole('button', { name: /筛选/ })
    fireEvent.click(filterBtn)
    fireEvent.change(screen.getAllByPlaceholderText('筛选...')[0], { target: { value: '田' } })
    expect(within(filterBtn).getByText('1')).toBeInTheDocument()
  })

  it('工具栏内置按钮是视图工具样式；筛选打开时 aria-pressed', async () => {
    await renderTable()
    const filterBtn = screen.getByRole('button', { name: /筛选/ })
    expect(filterBtn).toHaveAttribute('aria-pressed', 'false')
    expect(screen.getByRole('button', { name: '列' }).className).toContain('text-muted-foreground')
    expect(screen.getByRole('button', { name: '列' }).className).not.toMatch(/(^|\s)border(\s|$)/)
    fireEvent.click(filterBtn)
    expect(filterBtn).toHaveAttribute('aria-pressed', 'true')
  })

  it('区间列筛选行渲染「起 ~ 止」双输入（numberRange / dateRange）', async () => {
    const rangeCols: ColumnDef<Row, unknown>[] = [
      col('name', '姓名'),
      col('id', '编号', { meta: { filterVariant: 'numberRange' } }),
      col('city', '入职日期', { meta: { filterVariant: 'dateRange' } }),
    ]
    await renderTable({ columns: rangeCols })
    fireEvent.click(screen.getByRole('button', { name: /筛选/ }))
    // numberRange → ≥ / ≤；dateRange → 起 / 止
    expect(screen.getByPlaceholderText('≥')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('≤')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('起 yyyy-MM-dd')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('止')).toBeInTheDocument()
  })

  it('多选枚举列筛选行渲染多选触发按钮（multiSelect）', async () => {
    const msCols: ColumnDef<Row, unknown>[] = [
      col('name', '姓名'),
      col('status', '状态', {
        meta: {
          filterVariant: 'multiSelect',
          filterOptions: [
            { value: 'active', label: '启用' },
            { value: 'inactive', label: '停用' },
          ],
        },
      }),
    ]
    await renderTable({ columns: msCols })
    fireEvent.click(screen.getByRole('button', { name: /筛选/ }))
    // 未选时多选触发按钮显示「全部」
    expect(screen.getByRole('button', { name: '全部' })).toBeInTheDocument()
  })
})

describe('DataTable · 列显隐', () => {
  it('在「列」菜单取消勾选 → 该列从表格移除', async () => {
    await renderTable()
    // 用城市列唯一值「上海」断言（北京有两行，会触发 getByText 多元素报错）
    expect(screen.getByText('上海')).toBeInTheDocument()
    openColumnSettings()
    expect(screen.queryByText('拖拽可调整列顺序')).not.toBeInTheDocument()
    // 菜单项按列标题展示；点「城市」隐藏该列
    fireEvent.click(await screen.findByRole('checkbox', { name: '城市' }))
    await waitFor(() => expect(screen.queryByText('上海')).not.toBeInTheDocument())
  })

  it('列显隐持久化到 localStorage', async () => {
    await renderTable({ tableKey: 'persist-vis' })
    openColumnSettings()
    fireEvent.click(await screen.findByRole('checkbox', { name: '城市' }))
    await waitFor(() => {
      const stored = localStorage.getItem('swapmid-table:persist-vis:visibility')
      expect(stored && JSON.parse(stored).city).toBe(false)
    })
  })
})

describe('DataTable · 行选择 + bulkActions', () => {
  function withSelection(extra: Props = {}): Props {
    return {
      enableRowSelection: true,
      bulkActions: (rows, clear) => (
        <button type="button" onClick={clear}>
          批量删除 {rows.length}
        </button>
      ),
      ...extra,
    }
  }

  it('开启行选择后出现选择列', async () => {
    await renderTable(withSelection())
    expect(screen.getByLabelText('选择当前页')).toBeInTheDocument()
    expect(screen.getAllByLabelText('选择行')).toHaveLength(baseRows.length)
  })

  it('勾选行后显示「已选 N 条」并渲染 bulkActions', async () => {
    await renderTable(withSelection())
    fireEvent.click(screen.getAllByLabelText('选择行')[0])
    expect(screen.getByText('已选 1 条')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '批量删除 1' })).toBeInTheDocument()
  })

  it('表头全选勾选当前页所有行', async () => {
    await renderTable(withSelection())
    fireEvent.click(screen.getByLabelText('选择当前页'))
    expect(screen.getByText(`已选 ${baseRows.length} 条`)).toBeInTheDocument()
  })

  it('bulkActions 的 clearSelection 清空选择', async () => {
    await renderTable(withSelection())
    fireEvent.click(screen.getAllByLabelText('选择行')[0])
    fireEvent.click(screen.getByRole('button', { name: '批量删除 1' }))
    await waitFor(() => expect(screen.queryByText('已选 1 条')).not.toBeInTheDocument())
  })

  it('选中条替换右侧工具区，搜索框保留；「清空已选」后工具回来', async () => {
    await renderTable(withSelection())
    expect(screen.getByRole('button', { name: '列' })).toBeInTheDocument()
    fireEvent.click(screen.getAllByLabelText('选择行')[0])
    expect(screen.queryByRole('button', { name: '列' })).not.toBeInTheDocument()
    expect(screen.getByPlaceholderText('搜索...')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '清空已选' }))
    await waitFor(() => expect(screen.queryByText('已选 1 条')).not.toBeInTheDocument())
    expect(screen.getByRole('button', { name: '列' })).toBeInTheDocument()
  })

  it('受控 selectedRowIds + onSelectedRowIdsChange：勾选回调外部', async () => {
    const onChange = vi.fn()
    await renderTable(
      withSelection({ selectedRowIds: {}, onSelectedRowIdsChange: onChange, getRowId: (r) => r.id }),
    )
    fireEvent.click(screen.getAllByLabelText('选择行')[0])
    expect(onChange).toHaveBeenCalledWith({ '1': true })
  })
})

describe('DataTable · 服务端分页（manualPagination）', () => {
  function manualProps(extra: Props = {}): Props {
    return {
      data: manyRows.slice(0, 3),
      manualPagination: true,
      rowCount: 100,
      pageIndex: 0,
      manualPageSize: 15,
      onPaginationChange: vi.fn(),
      onSearchChange: vi.fn(),
      ...extra,
    }
  }

  it('不前端切片：data 原样全部渲染', async () => {
    const { container } = await renderTable(manualProps())
    expect(bodyRowCount(container)).toBe(3)
    expect(screen.getByText('员工1')).toBeInTheDocument()
    expect(screen.getByText('员工3')).toBeInTheDocument()
  })

  it('总条数用 rowCount 而非 data.length', async () => {
    const { container } = await renderTable(manualProps())
    expect(pagerText(container)).toContain('共 100 条')
  })

  it('隐藏列筛选按钮（改用页面级服务端筛选）', async () => {
    await renderTable(manualProps())
    expect(screen.queryByRole('button', { name: /筛选/ })).not.toBeInTheDocument()
  })

  it('翻页回调 onPaginationChange，pageIndex 递增', async () => {
    const onPaginationChange = vi.fn()
    const { container } = await renderTable(manualProps({ onPaginationChange }))
    fireEvent.click(pagerButtons(container)[2]) // 下一页
    expect(onPaginationChange).toHaveBeenCalledWith({ pageIndex: 1, pageSize: 15 })
  })

  it('搜索防抖 300ms 后回调 onSearchChange，挂载首帧不触发', async () => {
    const onSearchChange = vi.fn()
    await renderTable(manualProps({ onSearchChange }))
    // 首帧不应触发
    expect(onSearchChange).not.toHaveBeenCalled()
    fireEvent.change(screen.getByPlaceholderText('搜索...'), { target: { value: '员工' } })
    await waitFor(() => expect(onSearchChange).toHaveBeenCalledWith('员工'))
  })

  it('initialSearch 恢复搜索框显示（URL 的 q），且首帧不触发 onSearchChange', async () => {
    const onSearchChange = vi.fn()
    await renderTable(manualProps({ initialSearch: '员工2', onSearchChange }))
    expect(screen.getByPlaceholderText('搜索...')).toHaveValue('员工2') // 搜索框回填而非空
    expect(onSearchChange).not.toHaveBeenCalled() // 首帧不重复向服务端取数
  })

  it('loading 时浮层遮罩出现', async () => {
    const { container } = await renderTable(manualProps({ loading: true }))
    expect(container.querySelector('.animate-spin')).toBeInTheDocument()
  })

  // 服务端分页自定义档位默认走交互上限 200，而不是硬顶 5000。
  it('自定义档位上限默认钳到 200：拒绝 >200、提示文案随之变化、不写存储', async () => {
    const { container } = await renderTable(manualProps())
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数' }))
    expect(screen.getByText('本页最多 200 条，一次加载过多会变慢。')).toBeInTheDocument()
    fireEvent.change(screen.getByPlaceholderText('如 200'), { target: { value: '6000' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    expect(screen.getByText(/不能超过 200 条/)).toBeInTheDocument()
    expect(localStorage.getItem('swapmid-table:test:page-size-options')).toBeNull()
    expect(localStorage.getItem('swapmid-table:page-size-options')).toBeNull()
  })

  it('≤200 的档位正常添加并经 onPaginationChange 驱动取数', async () => {
    const onPaginationChange = vi.fn()
    const { container } = await renderTable(manualProps({ onPaginationChange }))
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数' }))
    fireEvent.change(screen.getByPlaceholderText('如 200'), { target: { value: '150' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:test:page-size-options') ?? '[]')).toContain(150),
    )
    expect(localStorage.getItem('swapmid-table:page-size-options')).toBeNull()
    expect(onPaginationChange).toHaveBeenCalledWith(expect.objectContaining({ pageSize: 150 }))
  })

  it('全局 500/1000 不进已添加，标明本页不可用', async () => {
    localStorage.setItem('swapmid-table:page-size-options', JSON.stringify([500, 1000]))
    const { container } = await renderTable(manualProps())
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数（有不可用档位）' }))
    expect(screen.getByText('本页最多 200 条，一次加载过多会变慢。')).toBeInTheDocument()
    expect(screen.queryByText('已添加')).not.toBeInTheDocument()
    expect(screen.getByText(/这些档位超出本页上限（200）/)).toBeInTheDocument()
    expect(screen.getByText('500')).toBeInTheDocument()
    expect(screen.getByText('1000')).toBeInTheDocument()
    expect(screen.getAllByTitle('超出本页上限，无法选用')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: '移除 500' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '不再显示' })).toBeInTheDocument()
    expect(screen.getByText('这些档位只从本页列表去掉，不会影响其他页面。')).toBeInTheDocument()
  })

  it('合法档位仍在已添加，超限档位单独成组', async () => {
    localStorage.setItem('swapmid-table:page-size-options', JSON.stringify([150, 500]))
    const { container } = await renderTable(manualProps())
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数（有不可用档位）' }))
    expect(screen.getByText('已添加')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '移除 150' })).toBeInTheDocument()
    expect(screen.getByText(/这些档位超出本页上限（200）/)).toBeInTheDocument()
    expect(screen.getByText('500')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '移除 500' })).not.toBeInTheDocument()
  })

  it('不再显示超限档位：写入空本表键，全局旧键保留', async () => {
    localStorage.setItem('swapmid-table:page-size-options', JSON.stringify([500, 1000]))
    const { container } = await renderTable(manualProps())
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数（有不可用档位）' }))
    fireEvent.click(screen.getByRole('button', { name: '不再显示' }))
    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:test:page-size-options') ?? 'null')).toEqual([]),
    )
    expect(JSON.parse(localStorage.getItem('swapmid-table:page-size-options') ?? '[]')).toEqual([500, 1000])
    expect(screen.queryByText(/这些档位超出本页上限/)).not.toBeInTheDocument()
  })

  it('在 200 上限页添加合法档位时不把全局 500/1000 写入本表键', async () => {
    localStorage.setItem('swapmid-table:page-size-options', JSON.stringify([500, 1000]))
    const { container } = await renderTable(manualProps())
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数（有不可用档位）' }))
    fireEvent.change(screen.getByPlaceholderText('如 200'), { target: { value: '150' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:test:page-size-options') ?? '[]')).toEqual([150]),
    )
    expect(JSON.parse(localStorage.getItem('swapmid-table:page-size-options') ?? '[]')).toEqual([500, 1000])
  })

  it('调用方 maxPageSize 覆盖默认：可加 400、拒 600，占位符跟随上限', async () => {
    const { container } = await renderTable(manualProps({ maxPageSize: 500 }))
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数' }))
    expect(screen.getByText('本页最多 500 条，一次加载过多会变慢。')).toBeInTheDocument()
    fireEvent.change(screen.getByPlaceholderText('如 500'), { target: { value: '400' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('swapmid-table:test:page-size-options') ?? '[]')).toContain(400),
    )
    fireEvent.click(within(container).getByRole('button', { name: '自定义每页条数' }))
    fireEvent.change(screen.getByPlaceholderText('如 500'), { target: { value: '600' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    expect(screen.getByText(/不能超过 500 条/)).toBeInTheDocument()
  })

  it('URL size 超上限时说明已从原值调整为本页上限', async () => {
    window.history.replaceState({}, '', '/test-table?size=1000')
    const { container } = await renderTable(manualProps())
    expect(pagerText(container)).toContain('已从 1000 调整为每页 200 条（本页上限）')
    fireEvent.click(screen.getByRole('button', { name: '知道了' }))
    expect(pagerText(container)).not.toContain('已从 1000 调整为每页 200 条')
    window.history.replaceState({}, '', '/test-table')
  })

  it('服务端分页跨会话记住每页条数：修改写入 localStorage，下次挂载在无 ?size= 时自动恢复并钳制上限', async () => {
    // 1. 添加并切换到 150 条 → 写入 swapmid-table:test:pageSize
    const firstChange = vi.fn()
    const first = await renderTable(manualProps({ onPaginationChange: firstChange }))
    fireEvent.click(within(first.container).getByRole('button', { name: '自定义每页条数' }))
    fireEvent.change(screen.getByPlaceholderText('如 200'), { target: { value: '150' } })
    fireEvent.click(screen.getByRole('button', { name: '添加' }))
    expect(localStorage.getItem('swapmid-table:test:pageSize')).toBe('150')
    first.unmount()

    // 2. 重新进入页面（无 URL ?size=），自动以 150 条触发一次 onPaginationChange
    const secondChange = vi.fn()
    const second = await renderTable(manualProps({ onPaginationChange: secondChange }))
    expect(secondChange).toHaveBeenCalledWith({ pageIndex: 0, pageSize: 150 })
    second.unmount()

    // 3. 若地址栏显式带 ?size=50（书签/分享链接），URL 优先、不覆盖为本地存的 150
    window.history.replaceState({}, '', '/test-table?size=50')
    const thirdChange = vi.fn()
    const third = await renderTable(manualProps({ manualPageSize: 50, onPaginationChange: thirdChange }))
    expect(thirdChange).not.toHaveBeenCalled()
    third.unmount()
    window.history.replaceState({}, '', '/test-table')
  })

  // 蓝图 §7：工具栏「刷新 / 重置」规范化——重置按钮条件显隐（viewDirty 门控）+ 刷新按钮 loading 反馈。
  describe('工具栏「刷新 / 重置」规范化', () => {
    it('viewDirty 未传时默认为 true（兼容旧手写 manual props 写法）：有 onResetView 即显示重置按钮，点击触发回调', async () => {
      const onResetView = vi.fn()
      await renderTable(manualProps({ onResetView }))
      const resetBtn = screen.getByRole('button', { name: '重置' })
      fireEvent.click(resetBtn)
      expect(onResetView).toHaveBeenCalledTimes(1)
    })

    it('viewDirty=false（默认视图）不渲染重置按钮，即便传了 onResetView', async () => {
      await renderTable(manualProps({ onResetView: vi.fn(), viewDirty: false }))
      expect(screen.queryByRole('button', { name: '重置' })).not.toBeInTheDocument()
    })

    it('viewDirty=true（视图偏离默认）渲染重置按钮', async () => {
      await renderTable(manualProps({ onResetView: vi.fn(), viewDirty: true }))
      expect(screen.getByRole('button', { name: '重置' })).toBeInTheDocument()
    })

    it('未传 onResetView 时不渲染重置按钮，无论 viewDirty 为何值', async () => {
      await renderTable(manualProps({ viewDirty: true }))
      expect(screen.queryByRole('button', { name: '重置' })).not.toBeInTheDocument()
    })

    it('重置按钮位于刷新按钮左侧（先「回默认」后「拉数据」）', async () => {
      const { container } = await renderTable(
        manualProps({ onResetView: vi.fn(), onReload: vi.fn(), viewDirty: true }),
      )
      const buttons = Array.from(container.querySelectorAll('button'))
      const resetIdx = buttons.findIndex((b) => b.textContent?.trim() === '重置')
      const reloadIdx = buttons.findIndex((b) => b.textContent?.includes('刷新数据'))
      expect(resetIdx).toBeGreaterThan(-1)
      expect(reloadIdx).toBeGreaterThan(-1)
      expect(resetIdx).toBeLessThan(reloadIdx)
    })

    it('loading=false 时刷新按钮可点击、图标不带 animate-spin', async () => {
      await renderTable(manualProps({ onReload: vi.fn(), loading: false }))
      const btn = screen.getByRole('button', { name: /刷新数据/ })
      expect(btn).not.toBeDisabled()
      expect(btn.querySelector('svg')?.classList.contains('animate-spin')).toBe(false)
    })

    it('loading=true 时刷新按钮 disabled 且图标带 animate-spin（防连点重复请求）', async () => {
      await renderTable(manualProps({ onReload: vi.fn(), loading: true }))
      const btn = screen.getByRole('button', { name: /刷新数据/ })
      expect(btn).toBeDisabled()
      expect(btn.querySelector('svg')?.classList.contains('animate-spin')).toBe(true)
    })
  })
})

describe('DataTable · compact 自动收缩高度', () => {
  it('单页且行数少（≤8）默认收缩：表格外壳为 shrink-0', async () => {
    const { container } = await renderTable()
    expect(tableShell(container).classList.contains('shrink-0')).toBe(true)
    expect(tableShell(container).classList.contains('flex-1')).toBe(false)
  })

  it('disableCompactHeight 强制 flex-1（吃满剩余高度）', async () => {
    const { container } = await renderTable({ disableCompactHeight: true })
    expect(tableShell(container).classList.contains('flex-1')).toBe(true)
    expect(tableShell(container).classList.contains('shrink-0')).toBe(false)
  })

  it('多页时不收缩：表格外壳为 flex-1', async () => {
    const { container } = await renderTable({ data: manyRows, pageSize: 3 })
    expect(tableShell(container).classList.contains('flex-1')).toBe(true)
  })
})

describe('DataTable · 跨页行选择（服务端模式）', () => {
  it('bulkActions 收全部跨页选中 id、「已选 N」用全部数（非仅当前页）', async () => {
    let receivedIds: string[] = []
    await renderTable({
      // 当前页只有 a，但受控 rowSelection 含跨页选中的 a/b/c
      data: [{ id: 'a', name: '甲', status: 'active', city: '北京' }],
      manualPagination: true, rowCount: 3, pageIndex: 0, manualPageSize: 15,
      onPaginationChange: vi.fn(), onSearchChange: vi.fn(),
      enableRowSelection: true,
      selectedRowIds: { a: true, b: true, c: true },
      getRowId: (r: Row) => r.id,
      bulkActions: (_rows, _clear, ids) => { receivedIds = ids; return <button>批量</button> },
    })
    // 「已选 3 条」= 全部跨页数（当前页仅 1 行），bulkActions 收到全部 3 个 id
    expect(screen.getByText('已选 3 条')).toBeInTheDocument()
    expect([...receivedIds].sort()).toEqual(['a', 'b', 'c'])
  })
})

describe('DataTable · 审计列（自动追加 / 默认隐藏）', () => {
  type AuditRow = Row & { creationTime: string; creatorName: string }
  // 含审计字段：创建时间 + 创建人姓名。刻意不给 creatorId（否则会触发账号扩展信息请求），
  // 姓名直接取自 creatorName，无需网络。
  const auditRows: AuditRow[] = [
    { id: '1', name: '田烨', status: 'active', city: '北京', creationTime: '2024-01-01T08:00:00Z', creatorName: '管理员' },
    { id: '2', name: '张三', status: 'inactive', city: '上海', creationTime: '2024-01-02T08:00:00Z', creatorName: '运维' },
  ]

  it('数据含审计字段时自动加「创建时间 / 创建人」列（列菜单可见）', async () => {
    await renderTable({ data: auditRows })
    openColumnSettings()
    expect(await screen.findByRole('checkbox', { name: '创建时间' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: '创建人' })).toBeInTheDocument()
  })

  it('审计列默认隐藏（表头不显示创建时间）', async () => {
    const { container } = await renderTable({ data: auditRows })
    const thead = container.querySelector('thead') as HTMLElement
    expect(within(thead).queryByText('创建时间')).not.toBeInTheDocument()
  })

  it('在列菜单勾选「创建人」后显示姓名', async () => {
    await renderTable({ data: auditRows })
    openColumnSettings()
    fireEvent.click(await screen.findByRole('checkbox', { name: '创建人' }))
    await waitFor(() => expect(screen.getByText('管理员')).toBeInTheDocument())
    expect(screen.getByText('运维')).toBeInTheDocument()
  })

  it('showAuditColumns=false 时不追加审计列', async () => {
    await renderTable({ data: auditRows, showAuditColumns: false })
    openColumnSettings()
    await screen.findByRole('checkbox', { name: '姓名' })
    expect(screen.queryByRole('checkbox', { name: '创建时间' })).not.toBeInTheDocument()
  })

  it('审计姓名只批量查询当前渲染页的用户 ID', async () => {
    const rows = Array.from({ length: 5 }, (_, index) => ({
      ...manyRows[index]!,
      creatorId: `00000000-0000-0000-0000-${(index + 1).toString().padStart(12, '0')}`,
    }))
    const fetchMock = vi.fn<typeof fetch>(async (_input, init) => {
      const body = JSON.parse(String(init?.body)) as { ids: string[] }
      return {
        ok: true,
        json: async () => ({ items: body.ids.map((id) => ({ id, displayName: id })) }),
      } as Response
    })
    vi.stubGlobal('fetch', fetchMock)

    renderWithIdentity(
      <DataTable columns={columns} data={rows} pageSize={2} tableKey="audit-current-page" />,
    )
    await screen.findByPlaceholderText('搜索...')
    openColumnSettings()
    fireEvent.click(await screen.findByRole('checkbox', { name: '创建人' }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const body = JSON.parse(String(fetchMock.mock.calls[0]?.[1]?.body)) as { ids: string[] }
    expect(body.ids).toEqual(rows.slice(0, 2).map((row) => row.creatorId))
  })
})

describe('DataTable · meta.defaultHidden（非审计字段的默认隐藏，账号角色 isPublic 列范本）', () => {
  it('meta.defaultHidden=true 的列默认隐藏，列菜单勾选后显示', async () => {
    const cols: ColumnDef<Row, unknown>[] = [
      col('name', '姓名'),
      col('city', '城市', { meta: { defaultHidden: true } }),
    ]
    const { container } = await renderTable({ columns: cols })
    const thead = container.querySelector('thead') as HTMLElement
    expect(within(thead).queryByText('城市')).not.toBeInTheDocument()

    openColumnSettings()
    fireEvent.click(await screen.findByRole('checkbox', { name: '城市' }))
    await waitFor(() => {
      const theadAfter = container.querySelector('thead') as HTMLElement
      expect(within(theadAfter).queryByText('城市')).toBeInTheDocument()
    })
  })

  it('未设置 defaultHidden 的普通列默认可见（不受新 meta 影响）', async () => {
    const { container } = await renderTable()
    const thead = container.querySelector('thead') as HTMLElement
    expect(within(thead).queryByText('城市')).toBeInTheDocument()
  })
})

describe('DataTable · 列拖拽排序', () => {
  it('拖拽表头重排列顺序并持久化到 localStorage', async () => {
    const { container } = await renderTable({ tableKey: 'drag-test' })
    const headers = container.querySelectorAll('thead th[draggable="true"]')
    expect(headers.length).toBe(3) // name / status / city，均为数据列可拖拽

    // 把第一列（name）拖到第三列（city）位置
    fireEvent.dragStart(headers[0])
    fireEvent.dragOver(headers[2])
    fireEvent.drop(headers[2])

    await waitFor(() => {
      const stored = localStorage.getItem('swapmid-table:drag-test:order')
      expect(stored).toBeTruthy()
      const order = JSON.parse(stored as string) as string[]
      expect(order.indexOf('name')).toBeGreaterThan(order.indexOf('city'))
    })
  })

  it('结构列（选择列）不可拖拽', async () => {
    const { container } = await renderTable({ enableRowSelection: true })
    const firstHeader = container.querySelector('thead th') // 选择列在最左
    expect(firstHeader?.getAttribute('draggable')).not.toBe('true')
  })
})

describe('DataTable · 工具栏按钮上限', () => {
  it('导入、导出、重置、刷新、筛选、列同时存在时，可见文字按钮不超过 4 个，低频进「更多」', async () => {
    await renderTable({
      onImportRows: vi.fn(),
      onReload: vi.fn(),
      onResetView: vi.fn(),
      viewDirty: true,
    })
    expect(screen.getByRole('button', { name: '导出' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /筛选/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /刷新数据/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '更多' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '导入' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '列' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '重置' })).not.toBeInTheDocument()
    const toolbarButtons = ['导出', '筛选', '刷新数据', '更多']
    expect(toolbarButtons).toHaveLength(4)

    fireEvent.keyDown(screen.getByRole('button', { name: '更多' }), { key: 'Enter' })
    expect(await screen.findByRole('menuitem', { name: '导入' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: '重置' })).toBeInTheDocument()
  })
})

describe('DataTable · 数字列右对齐', () => {
  it('标题为数量、金额的列，表头和单元格右对齐', async () => {
    const numericColumns: ColumnDef<Row, unknown>[] = [
      col('name', '名称'),
      col('city', '数量', { cell: ({ row }) => row.original.city.length }),
      {
        accessorKey: 'id',
        header: ({ column }) => <DataTableColumnHeader column={column} title="金额" />,
        cell: ({ row }) => row.original.id,
      },
    ]
    const { container } = await renderTable({ columns: numericColumns })
    const headers = Array.from(container.querySelectorAll('thead th'))
    const qtyHeader = headers.find((header) => header.textContent?.includes('数量'))
    const amountHeader = headers.find((header) => header.textContent?.includes('金额'))
    const nameHeader = headers.find((header) => header.textContent?.includes('名称'))
    expect(qtyHeader?.className).toContain('text-right')
    expect(amountHeader?.className).toContain('text-right')
    expect(nameHeader?.className).not.toContain('text-right')

    const bodyRow = container.querySelector('tbody tr')
    const cells = Array.from(bodyRow?.querySelectorAll('td') ?? [])
    expect(cells[1]?.className).toContain('text-right')
    expect(cells[1]?.className).toContain('tabular-nums')
    expect(cells[2]?.className).toContain('text-right')
    expect(cells[0]?.className ?? '').not.toContain('text-right')
  })
})
