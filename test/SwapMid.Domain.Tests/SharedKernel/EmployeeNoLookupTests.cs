using System.Linq;
using SwapMid.SharedKernel.Employees;
using Shouldly;
using Xunit;

namespace SwapMid.SharedKernel;

public class EmployeeNoLookupTests
{
    [Fact]
    public void Expand_带默认前缀_同时给出数字段()
    {
        var keys = EmployeeNoLookup.Expand("SM005353");
        keys.ShouldContain("SM005353");
        keys.ShouldContain("005353");
    }

    [Fact]
    public void Expand_纯数字_同时给出默认前缀()
    {
        var keys = EmployeeNoLookup.Expand("005353");
        keys.ShouldContain("005353");
        keys.ShouldContain("SM005353");
    }

    [Fact]
    public void Expand_空白_返回空()
    {
        EmployeeNoLookup.Expand("  ").ShouldBeEmpty();
        EmployeeNoLookup.Expand(null).ShouldBeEmpty();
    }

    [Fact]
    public void Expand_大小写不敏感去重()
    {
        EmployeeNoLookup.Expand("sm005353")
            .Count(item => string.Equals(item, "SM005353", System.StringComparison.OrdinalIgnoreCase))
            .ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Expand_数字段前导零_与去零工号对照()
    {
        var keys = EmployeeNoLookup.Expand("SM005194");
        keys.ShouldContain("SM005194");
        keys.ShouldContain("005194");
        keys.ShouldContain("5194");
        keys.ShouldContain("SM5194");
    }

    [Fact]
    public void Canonical_前导零变体合成同一工号()
    {
        EmployeeNoLookup.Canonical("SM005194").ShouldBe("SM5194");
        EmployeeNoLookup.Canonical("SM5194").ShouldBe("SM5194");
        EmployeeNoLookup.Canonical("005194").ShouldBe("SM5194");
        EmployeeNoLookup.Canonical("集成架构组").ShouldBe("集成架构组");
    }

    [Fact]
    public void Index_前导零变体能对上同一个人()
    {
        var people = new[] { new Person("SM5194") };
        var map = EmployeeNoLookup.Index(people, p => p.No);
        map["SM005194"].No.ShouldBe("SM5194");
        map["5194"].No.ShouldBe("SM5194");
    }

    [Fact]
    public void Index_两条相同工号不同对象_展开键丢弃()
    {
        var people = new[] { new Person("SM1"), new Person("SM1") };
        var map = EmployeeNoLookup.Index(people, p => p.No);
        map.ContainsKey("SM1").ShouldBeFalse();
    }

    private sealed record Person(string No);
}
