using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using SwapMid.SharedKernel;
using Shouldly;
using Xunit;

namespace SwapMid.Architecture;

/// <summary>
/// 批量写入契约的资源边界门禁（2026-08-04）。
///
/// 守的是一条**没有它就必然复发**的规则：任何「收一组 Id 然后逐条处理」的入参 DTO 都必须声明
/// 上限。缺了上限，一个手工构造的百万 id 请求会一路走到逐条 <c>FindAsync + DeleteAsync</c>，
/// 在数据库上跑成两百万次往返——请求本身就是 DoS，而且**编译、启动、单测全绿**，只有真被打了
/// 才知道。
///
/// ⚠️ 门禁只认「属性名叫 Ids 的 Guid 集合」这一形状——它覆盖当前全部批量端点，但**不是**万能的：
/// 新增批量契约若用了别的属性名（如 <c>EmployeeIds</c>），本守卫看不见。所以它是兜底而非替代，
/// 写新批量 DTO 时仍要自己记得加 <c>[MaxLength]</c>。
/// </summary>
public class BatchInputBoundaryTests
{
    private static IEnumerable<(Type Dto, PropertyInfo Property)> BatchIdProperties()
    {
        var assembly = typeof(BatchIdsInput).Assembly;
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!string.Equals(property.Name, "Ids", StringComparison.Ordinal)) continue;
                if (!typeof(IEnumerable).IsAssignableFrom(property.PropertyType)) continue;
                // 只认 Guid 集合：字符串集合（如外部异构系统的复合业务键）另有边界，不在本门禁口径内。
                var elementType = property.PropertyType.IsGenericType
                    ? property.PropertyType.GetGenericArguments().FirstOrDefault()
                    : null;
                if (elementType != typeof(Guid)) continue;
                yield return (type, property);
            }
        }
    }

    [Fact]
    public void 所有批量Id入参都必须声明上限()
    {
        var missing = BatchIdProperties()
            .Where(x => x.Property.GetCustomAttribute<MaxLengthAttribute>() == null)
            .Select(x => $"{x.Dto.FullName}.{x.Property.Name}")
            .ToList();

        missing.ShouldBeEmpty(
            "这些批量入参没有 [MaxLength]，一个超大 payload 就能把它变成 DoS——"
            + $"请加 [MaxLength(BatchOperationLimits.MaxIdsPerRequest)]：{string.Join("、", missing)}");
    }

    /// <summary>
    /// 上限值必须统一走 <see cref="BatchOperationLimits.MaxIdsPerRequest"/>，不许各写各的字面量。
    ///
    /// 理由是**跨层契约**：前端分片大小（`lib/crud/batch-chunk.ts` 的 `BATCH_CHUNK_SIZE`）锚的就是
    /// 这个值。各 DTO 各设一个数，前端只能按最小的那个分片（否则某些端点每片都被整批拒绝），
    /// 而「最小的那个是多少」没有任何地方能查——这种不一致不会报错，只会在某个页面上表现为
    /// 「批量操作莫名其妙失败」。
    /// </summary>
    [Fact]
    public void 批量Id上限必须取自统一常量()
    {
        var deviating = BatchIdProperties()
            .Select(x => (x.Dto, x.Property, Max: x.Property.GetCustomAttribute<MaxLengthAttribute>()))
            .Where(x => x.Max != null && x.Max.Length != BatchOperationLimits.MaxIdsPerRequest)
            .Select(x => $"{x.Dto.FullName}.{x.Property.Name}={x.Max!.Length}")
            .ToList();

        deviating.ShouldBeEmpty(
            $"批量 Id 上限必须统一为 {BatchOperationLimits.MaxIdsPerRequest}"
            + $"（前端分片大小锚的是它）：{string.Join("、", deviating)}");
    }

    /// <summary>
    /// 上限必须**显著大于**前端单页上限，否则「跨页勾选 + 全选」这条既有动线一点就 400。
    ///
    /// 这条断言把那个真实事故钉死：原方案取 200，而服务端分页上限是 5000（每页档位用户可调），
    /// 把每页调到 5000 再点一次全选就是 5000 个 id——**一次完全正常的 UI 操作**。
    /// 现在靠前端分片消化，所以上限只需覆盖「单片」；但它仍不能小到让分片变得毫无意义。
    /// </summary>
    [Fact]
    public void 上限须足以承载一次有意义的批量()
    {
        BatchOperationLimits.MaxIdsPerRequest.ShouldBeGreaterThanOrEqualTo(1000,
            "上限太小会让分片退化成几十次往返，用户等待时间反而更长");
    }
}
