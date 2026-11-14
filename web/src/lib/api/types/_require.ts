/**
 * 把 T 中的 K 字段改为必填且去掉 null。
 * 适用于领域上必有值、仅因 C# 引用类型默认可空而被 Swagger 标 nullable 的字段
 * （主键、Check.NotNull 的编码/名称、非空枚举/时间等）。
 */
export type Require<T, K extends keyof T> = Omit<T, K> & { [P in K]-?: NonNullable<T[P]> }
