namespace SwapMid.AccountManagement;

/// <summary>账号权限域的资源边界常量。</summary>
public static class AccountManagementConsts
{
    public static class Users
    {
        /// <summary>
        /// 账号列表单页返回上限（服务端<strong>静默钳制</strong>，不报错）。
        ///
        /// 全局 <c>LimitedResultRequestDto.MaxMaxResultCount</c> 是 5000（给下拉/批量加载留的），
        /// 对交互列表过大：旧实现还按行调 <c>GetRolesAsync</c>，一页 1000 就是约 1000 次往返。
        /// 角色装配已改为当页一次批量查询，但 JSON、TanStack 行模型仍随页大小线性涨。
        /// 「一次操作很多行」走跨页勾选 + 批量分片，不靠把一页撑大。
        ///
        /// 钳制而非拒绝：旧收藏 URL 带 <c>?size=1000</c> 是常态，为此抛异常等于无故炸掉正常访问。
        /// ⚠️ 跨层契约：前端 <c>ACCOUNT_USER_LIST_MAX_PAGE_SIZE</c>
        /// （<c>web-react/.../account-management/users/constants.ts</c>）必须与本值一致。
        /// </summary>
        public const int ListMaxPageSize = 200;

        /// <summary>把请求的每页条数钳到 <see cref="ListMaxPageSize"/>（调用方已通过 ABP 校验 ≥1）。</summary>
        public static int ClampListPageSize(int requested)
            => requested < ListMaxPageSize ? requested : ListMaxPageSize;
    }
}
