namespace SwapMid;

/// <summary>
/// PR CI 用 <c>--filter Category!=Slow</c> 跳过的分类常量。
/// xunit 的 <c>TraitAttribute</c> 是 sealed，不能派生；测试类直接写
/// <c>[Trait(SlowTest.CategoryName, SlowTest.CategoryValue)]</c>。
/// </summary>
public static class SlowTest
{
    public const string CategoryName = "Category";
    public const string CategoryValue = "Slow";
}
