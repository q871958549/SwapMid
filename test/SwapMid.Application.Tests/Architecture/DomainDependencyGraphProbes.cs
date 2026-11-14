// DomainDependencyGraphTests 的 IL 扫描器自检探针：每个 Via* 命名空间只用一种方式引用 Target。
// 位于测试程序集，不在正式扫描范围（正式扫描只读 src 的四个程序集）。
// 命名空间内写 Target.TargetType 是部分限定名——源码级扫描看不到，正是 IL 扫描要补的那一路。
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwapMid.ArchitectureProbes.Target
{
    public class TargetType
    {
        public int Value;

        public static int Compute() => 1;
    }

    public static class TargetConsts
    {
        public const string Code = "probe";
    }
}

namespace SwapMid.ArchitectureProbes.Neutral
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MarkerAttribute(Type type) : Attribute
    {
        public Type Type { get; } = type;
    }
}

namespace SwapMid.ArchitectureProbes.ViaField
{
    public class Probe
    {
        public Target.TargetType? Field;
    }
}

namespace SwapMid.ArchitectureProbes.ViaBaseType
{
    public class Probe : Target.TargetType
    {
    }
}

namespace SwapMid.ArchitectureProbes.ViaGenericArgument
{
    public class Probe
    {
        public List<Target.TargetType> Items { get; } = new();
    }
}

namespace SwapMid.ArchitectureProbes.ViaMethodBody
{
    public class Probe
    {
        public int Run() => Target.TargetType.Compute();
    }
}

namespace SwapMid.ArchitectureProbes.ViaAsyncStateMachine
{
    public class Probe
    {
        // 引用只出现在编译器生成的嵌套状态机里，验证「嵌套类型归最外层声明类型」。
        public async Task<int> RunAsync()
        {
            await Task.Yield();
            return Target.TargetType.Compute();
        }
    }
}

namespace SwapMid.ArchitectureProbes.ViaAttributeTypeof
{
    [Neutral.Marker(typeof(Target.TargetType))]
    public class Probe
    {
    }
}

namespace SwapMid.ArchitectureProbes.ConstOnly
{
    public class Probe
    {
        // const 编译期内联为 ldstr，IL 里不留任何引用——这是 IL 扫描的已知盲区，由源码级扫描兜底。
        public string Code() => Target.TargetConsts.Code;
    }
}

namespace SwapMid.ArchitectureProbes.Isolated
{
    public class Probe
    {
        public int Value;
    }
}
