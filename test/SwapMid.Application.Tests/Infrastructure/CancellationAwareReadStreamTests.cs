using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SwapMid.Infrastructure;
using Shouldly;
using Xunit;

namespace SwapMid.InfrastructureTests;

public class CancellationAwareReadStreamTests
{
    [Fact]
    public void ExcelDataReader同步读取期间发生取消时必须立即停止()
    {
        using var cancellation = new CancellationTokenSource();
        using var inner = new CancelAfterReadStream(cancellation, new byte[] { 1, 2, 3 });
        using var stream = new CancellationAwareReadStream(inner, cancellation.Token);
        var buffer = new byte[3];

        Should.Throw<OperationCanceledException>(() => stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task CopyToAsync调用方未传令牌时构造令牌取消仍应停止()
    {
        using var cancellation = new CancellationTokenSource();
        await using var inner = new CancelAfterReadStream(cancellation, new byte[] { 1, 2, 3 });
        await using var stream = new CancellationAwareReadStream(inner, cancellation.Token);
        await using var destination = new MemoryStream();

        await Should.ThrowAsync<OperationCanceledException>(
            () => stream.CopyToAsync(destination));
    }

    [Fact]
    public void LeaveOpen为False时释放包装器必须释放底层流()
    {
        using var inner = new MemoryStream(new byte[] { 1, 2, 3 });
        var stream = new CancellationAwareReadStream(
            inner,
            CancellationToken.None,
            leaveOpen: false);

        stream.Dispose();

        inner.CanRead.ShouldBeFalse();
    }

    private sealed class CancelAfterReadStream(
        CancellationTokenSource cancellation,
        byte[] buffer) : MemoryStream(buffer)
    {
        public override int Read(byte[] target, int offset, int count)
        {
            var read = base.Read(target, offset, count);
            cancellation.Cancel();
            return read;
        }

        public override int Read(Span<byte> target)
        {
            var read = base.Read(target);
            cancellation.Cancel();
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> target,
            CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(target, cancellationToken);
            cancellation.Cancel();
            return read;
        }
    }
}
