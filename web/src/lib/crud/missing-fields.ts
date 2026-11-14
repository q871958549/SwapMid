/**
 * 把「字段显示名 → 是否已填」的声明收敛成缺失项数组，供 `CrudFormDialog` 的
 * `missingFields` 与 `canSubmit` **同源**消费：
 *
 * ```tsx
 * const missing = useMemo(() => missingLabels({
 *   [t('common.belongEnterpriseLabel')]: !!crud.form.enterpriseId,
 *   [t('position.codeLabel')]: !!crud.form.code,
 * }), [crud.form, t])
 *
 * <CrudFormDialog crud={crud} title={...} canSubmit={missing.length === 0} missingFields={missing}>
 * ```
 *
 * 一处声明、两处消费，杜绝「`canSubmit` 里判了四个字段、提示里只列了三个」这类漂移
 * ——两份手写清单迟早会走散，而走散的表现是**提示说齐了但按钮仍灰着**，比没有提示更费解。
 *
 * 键是**已翻译的显示名**、值是「填了吗」（`true` = 已填）。写成对象字面量而非数组，
 * 是为了让标签与条件在源码里左右对齐、一眼能核对完整性。
 *
 * ⚠️ 只表达「非空」这一档。范围、互斥、异步查重等条件仍留在调用方自己的 `canSubmit` 里
 * ——硬把它们塞进来就得编造「金额（须大于 0）」这种把校验规则写进字段名的标签。
 */
export function missingLabels(fields: Record<string, boolean>): string[] {
  return Object.entries(fields)
    .filter(([, filled]) => !filled)
    .map(([label]) => label)
}
