using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;

namespace SwapMid;

/// <summary>
/// 上传流限长拷贝护栏：
/// <c>ContentLength</c> 是客户端申报值，只能预检；所有实际读取都必须边读边计数，
/// 超限立即中断，不能先把完整载荷写入内存或磁盘后再复核。
/// </summary>
public static class UploadStreamGuard
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// 把上传流限长拷入 <see cref="MemoryStream"/>（返回时位置已归零）；
    /// 实际读取字节数超过 <paramref name="maxBytes"/> 时立即抛 <see cref="UserFriendlyException"/>。
    /// </summary>
    public static async Task<MemoryStream> CopyToMemoryBoundedAsync(
        Stream source,
        long maxBytes,
        string overLimitMessage,
        CancellationToken cancellationToken = default)
    {
        var memory = new MemoryStream();
        try
        {
            await CopyBoundedAsync(
                source,
                memory,
                maxBytes,
                overLimitMessage,
                cancellationToken);
        }
        catch
        {
            await memory.DisposeAsync();
            throw;
        }

        memory.Position = 0;
        return memory;
    }

    /// <summary>
    /// 把上传流限长拷入目标流并返回实际写入字节数；超过上限时在写入超额缓冲区前立即中断。
    /// </summary>
    public static async Task<long> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        string overLimitMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var buffer = new byte[BufferSize];
        long totalBytes = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (read > maxBytes - totalBytes)
            {
                throw new UserFriendlyException(overLimitMessage);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalBytes += read;
        }

        return totalBytes;
    }
}
