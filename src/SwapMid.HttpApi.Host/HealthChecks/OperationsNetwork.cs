using System.Net;
using System.Net.Sockets;

namespace SwapMid.HealthChecks;

/// <summary>
/// 判断就绪探针是否可以对调用方展开依赖明细。
/// 公网只需要状态码；数据库、缓存、队列的名称与失败原因只给私网或回环来源。
/// </summary>
public static class OperationsNetwork
{
    public static bool IsPrivateOrLoopback(IPAddress? address)
    {
        if (address == null)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.IsIPv6LinkLocal)
        {
            return true;
        }

        var ipv6 = address.GetAddressBytes();
        return (ipv6[0] & 0xFE) == 0xFC;
    }
}
