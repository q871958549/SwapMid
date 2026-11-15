import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { NextIntlClientProvider } from 'next-intl'
import zhHansMessages from '../../../../../messages/zh-Hans.json'
import { PipelineFieldMapperPanel } from './pipeline-field-mapper'

function renderPanel(
  props: Partial<React.ComponentProps<typeof PipelineFieldMapperPanel>> = {},
) {
  const onChange = props.onChange ?? vi.fn()
  const utils = render(
    <NextIntlClientProvider locale="zh-Hans" messages={zhHansMessages}>
      <PipelineFieldMapperPanel
        sourceColumns={props.sourceColumns ?? [
          { name: 'src_id', dataType: 'int4', isNullable: false },
          { name: 'customer_name', dataType: 'varchar(128)', isNullable: false },
          { name: 'created_at', dataType: 'timestamp', isNullable: true },
        ]}
        targetColumns={props.targetColumns ?? [
          { name: 'src_id', dataType: 'int4', isNullable: false, isPrimaryKey: true, isUniqueIndexed: true },
          { name: 'CustomerName', dataType: 'varchar(128)', isNullable: false },
          { name: 'note', dataType: 'text', isNullable: true },
        ]}
        columnMappings={props.columnMappings ?? []}
        onChange={onChange}
      />
    </NextIntlClientProvider>,
  )
  return { ...utils, onChange }
}

describe('PipelineFieldMapperPanel', () => {
  it('渲染探测到的目标列与自动匹配按钮并触发自动匹配回调', () => {
    const { onChange } = renderPanel()

    expect(screen.getByText('已映射 0 / 3')).toBeInTheDocument()
    expect(screen.getByText('src_id')).toBeInTheDocument()
    expect(screen.getByText('CustomerName')).toBeInTheDocument()
    expect(screen.getByText('note')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '自动匹配' }))
    expect(onChange).toHaveBeenCalledWith([
      { sourceColumn: 'src_id', targetColumn: 'src_id' },
      { sourceColumn: 'customer_name', targetColumn: 'CustomerName' },
    ])
  })

  it('通过 Select 修改目标列映射后按目标列顺序回传最新映射数组', () => {
    const { onChange } = renderPanel({
      columnMappings: [{ sourceColumn: 'src_id', targetColumn: 'src_id' }],
    })

    expect(screen.getByText('已映射 1 / 3')).toBeInTheDocument()

    const customerTrigger = screen.getByRole('combobox', { name: 'CustomerName' })
    fireEvent.keyDown(customerTrigger, { key: 'ArrowDown' })
    const option = screen.getByRole('option', { name: 'customer_name (varchar(128))' })
    fireEvent.click(option)

    expect(onChange).toHaveBeenCalledWith([
      { sourceColumn: 'src_id', targetColumn: 'src_id' },
      { sourceColumn: 'customer_name', targetColumn: 'CustomerName' },
    ])
  })

  it('显示类型差异与缺失源列警告并支持清空映射', () => {
    const { onChange } = renderPanel({
      sourceColumns: [
        { name: 'src_id', dataType: 'varchar(32)', isNullable: false },
      ],
      targetColumns: [
        { name: 'src_id', dataType: 'int4', isNullable: false, isPrimaryKey: true },
        { name: 'required_col', dataType: 'text', isNullable: false },
      ],
      columnMappings: [
        { sourceColumn: 'src_id', targetColumn: 'src_id' },
        { sourceColumn: 'removed_col', targetColumn: 'required_col' },
      ],
    })

    expect(screen.getByText('类型差异')).toBeInTheDocument()
    expect(screen.getByText('源列缺失')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: '清空映射' }))
    expect(onChange).toHaveBeenCalledWith([])
  })
})
