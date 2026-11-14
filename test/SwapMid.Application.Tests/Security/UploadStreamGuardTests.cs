using System;
using System.IO;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.Security;

/// <summary>
/// 上传流限长拷贝护栏测试（安全收口 2026-07-11 Y-1）：
/// 核心断言是「超限即断」——不能等全量读完再复核，超额字节不进内存。
/// </summary>
public class UploadStreamGuardTests
{
    private const string OverLimit = "文件超过上限";

    [Fact]
    public async Task 未超限_全量拷贝且位置归零()
    {
        var payload = new byte[300_000];
        new Random(42).NextBytes(payload);

        using var ms = await UploadStreamGuard.CopyToMemoryBoundedAsync(
            new MemoryStream(payload), maxBytes: 300_000, OverLimit);

        ms.Position.ShouldBe(0);
        ms.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task 超限_立即抛出且不再继续读取()
    {
        var source = new CountingStream(totalBytes: 100 * 1024 * 1024); // 申报可读 100MB
        var ex = await Should.ThrowAsync<UserFriendlyException>(() =>
            UploadStreamGuard.CopyToMemoryBoundedAsync(source, maxBytes: 1024 * 1024, OverLimit));

        ex.Message.ShouldBe(OverLimit);
        // 只允许读到上限附近（上限 + 一个读缓冲），证明没有全量吞下 100MB
        source.BytesRead.ShouldBeLessThanOrEqualTo(1024 * 1024 + 128 * 1024);
    }

    [Fact]
    public async Task 空流_返回空内存流()
    {
        using var ms = await UploadStreamGuard.CopyToMemoryBoundedAsync(
            new MemoryStream(), maxBytes: 1024, OverLimit);

        ms.Length.ShouldBe(0);
    }

    [Fact]
    public async Task 目标流拷贝_返回实际字节数且保持内容一致()
    {
        var payload = new byte[150_000];
        new Random(84).NextBytes(payload);
        await using var destination = new MemoryStream();

        var actualBytes = await UploadStreamGuard.CopyBoundedAsync(
            new MemoryStream(payload),
            destination,
            maxBytes: payload.Length,
            OverLimit);

        actualBytes.ShouldBe(payload.LongLength);
        destination.ToArray().ShouldBe(payload);
    }

    [Fact]
    public async Task 目标流超限_不得写入超额缓冲区()
    {
        var source = new CountingStream(totalBytes: 100 * 1024 * 1024);
        await using var destination = new MemoryStream();

        await Should.ThrowAsync<UserFriendlyException>(() =>
            UploadStreamGuard.CopyBoundedAsync(
                source,
                destination,
                maxBytes: 1024 * 1024,
                OverLimit));

        destination.Length.ShouldBeLessThanOrEqualTo(1024 * 1024);
        source.BytesRead.ShouldBeLessThanOrEqualTo(1024 * 1024 + 128 * 1024);
    }

    /// <summary>只产出零字节、记录累计读取量的假流——模拟超大请求体而不真占内存。</summary>
    private sealed class CountingStream : Stream
    {
        private readonly long _totalBytes;

        public CountingStream(long totalBytes) => _totalBytes = totalBytes;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _totalBytes - BytesRead;
            var toRead = (int)Math.Min(count, remaining);
            Array.Clear(buffer, offset, toRead);
            BytesRead += toRead;
            return toRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
