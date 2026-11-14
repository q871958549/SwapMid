/** 字典维护成功后刷新同页的消费者，不缓存用户身份相关的选项。 */
export const DICTIONARY_CHANGED_EVENT = 'swapmid:dictionary-changed'

export function notifyDictionaryChanged(code: string) {
  window.dispatchEvent(new CustomEvent(DICTIONARY_CHANGED_EVENT, { detail: code }))
}
