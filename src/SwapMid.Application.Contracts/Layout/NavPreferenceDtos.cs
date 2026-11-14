namespace SwapMid.Layout;

/// <summary>当前用户生效的导航顺序偏好。</summary>
public class NavOrderDto
{
    /// <summary>当前用户生效的导航顺序 JSON（User→Global→默认 解析后）。</summary>
    public string? Value { get; set; }

    /// <summary>当前用户是否 admin（admin 的保存写入全局默认，其余写入个人）。</summary>
    public bool IsAdmin { get; set; }
}

/// <summary>保存导航顺序入参。</summary>
public class SaveNavOrderInput
{
    /// <summary>导航顺序 JSON。</summary>
    public string? Value { get; set; }
}
