namespace SwapMid.EnterpriseManagement;

public static class EnterpriseConsts
{
    public const int MaxCodeLength = 64;
    public const int MaxNameLength = 128;
    public const int MaxShortNameLength = 64;
    public const int MaxContactPersonLength = 64;
    public const int MaxContactPhoneLength = 32;
    public const int MaxEmailLength = 128;
    public const int MaxAddressLength = 256;

    /// <summary>
    /// 员工<strong>交互列表页</strong>的每页档位上限（前端 DataTable / URL size 使用）。
    /// 不能套到 <c>EmployeeAppService.GetListAsync</c> 上：同一接口还是全站员工下拉的数据源
    /// （<c>maxResultCount=1000/2000</c>），服务端钳制会让下拉静默少人。
    /// ⚠️ 跨层契约：前端 <c>EMPLOYEE_LIST_MAX_PAGE_SIZE</c> 必须与本值一致。
    /// </summary>
    public const int EmployeeListMaxPageSize = 200;
}
