using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SwapMid.Infrastructure;

/// <summary>
/// 为未显式传递令牌的同步/异步读取补充请求取消检查。默认由调用方持有底层流；
/// 当 <c>leaveOpen=false</c> 时，释放包装器也会释放底层流（例如触发临时文件的 DeleteOnClose）。
/// </summary>
internal sealed class CancellationAwareReadStream(
    Stream inner,
    CancellationToken cancellationToken,
    bool leaveOpen = true) : Stream
{
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inner.Position = value;
        }
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = _inner.Read(buffer, offset, count);
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = _inner.Read(buffer);
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override int ReadByte()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = _inner.ReadByte();
        cancellationToken.ThrowIfCancellationRequested();
        return value;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken requestCancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            requestCancellationToken);
        var read = await _inner.ReadAsync(buffer, linkedCancellation.Token);
        cancellationToken.ThrowIfCancellationRequested();
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
