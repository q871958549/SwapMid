export function defaultItemValue(label: string, value: string): string {
  return value.trim() || label.trim()
}

export function nextItemSortOrder(items: readonly { sortOrder: number }[]): number {
  return items.reduce((max, item) => Math.max(max, item.sortOrder), 0) + 1
}
