using System;
using SwapMid.SharedKernel.Employees;
using Shouldly;
using Xunit;

namespace SwapMid.SharedKernel;

public class EmployeeNamePinyinTests
{
    [Fact]
    public void LooksLikeInitialsQuery_纯字母才算()
    {
        EmployeeNamePinyin.LooksLikeInitialsQuery("ty").ShouldBeTrue();
        EmployeeNamePinyin.LooksLikeInitialsQuery("SM").ShouldBeTrue();
        EmployeeNamePinyin.LooksLikeInitialsQuery("唐远").ShouldBeFalse();
        EmployeeNamePinyin.LooksLikeInitialsQuery("sm005353").ShouldBeFalse();
        EmployeeNamePinyin.LooksLikeInitialsQuery("138").ShouldBeFalse();
        EmployeeNamePinyin.LooksLikeInitialsQuery("").ShouldBeFalse();
    }

    [Fact]
    public void Initials_中文姓名取首字母()
    {
        EmployeeNamePinyin.Initials("唐远").ShouldBe("ty");
        EmployeeNamePinyin.Initials("何若涵").ShouldBe("hrh");
    }

    [Fact]
    public void InitialsContain_输入首字母命中姓名()
    {
        EmployeeNamePinyin.InitialsContain("唐远", "ty").ShouldBeTrue();
        EmployeeNamePinyin.InitialsContain("唐远", "TY").ShouldBeTrue();
        EmployeeNamePinyin.InitialsContain("唐远", "t").ShouldBeTrue();
        EmployeeNamePinyin.InitialsContain("唐远", "zs").ShouldBeFalse();
    }

    [Fact]
    public void MatchIds_纯字母查询返回命中项()
    {
        var ty = Guid.NewGuid();
        var zs = Guid.NewGuid();
        var matched = EmployeeNamePinyin.MatchIds(
            [(ty, "唐远"), (zs, "张三")],
            "ty");

        matched.ShouldBe([ty]);
        EmployeeNamePinyin.MatchIds([(ty, "唐远")], "唐").ShouldBeEmpty();
    }
}
