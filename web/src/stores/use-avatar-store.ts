import { create } from 'zustand'

/**
 * 头像版本号：header 与个人中心共享。上传成功后 bump → 两处 <AvatarImage> 的 src 都带上新的 ?v= 破缓存，
 * 一起重新拉取新头像。
 *
 * 为什么需要它：header 是 dashboard layout 的**持久组件**，在别的页面（/profile）上传头像后，
 * router.refresh() 只刷 RSC、不改 client 组件的 <img src>、也不 remount header——只有订阅本 store、
 * 让 src 真正变化，浏览器才会重新请求。version=0 时不带 ?v，走默认 URL（首屏正常请求 /me）。
 */
interface AvatarState {
  version: number
  bump: () => void
}

export const useAvatarStore = create<AvatarState>((set) => ({
  version: 0,
  bump: () => set({ version: Date.now() }),
}))
