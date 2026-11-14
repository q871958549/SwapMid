using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.DependencyInjection;

namespace SwapMid;

/// <summary>
/// 保留 Kestrel 已判定的协议级 4xx 状态。ABP 默认 finder 不识别
/// <see cref="BadHttpRequestException"/>，会把请求体超限等客户端错误误包装成 500。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IHttpExceptionStatusCodeFinder))]
public sealed class SwapMidHttpExceptionStatusCodeFinder :
    DefaultHttpExceptionStatusCodeFinder
{
    public SwapMidHttpExceptionStatusCodeFinder(
        IOptions<AbpExceptionHttpStatusCodeOptions> options)
        : base(options)
    {
    }

    public override HttpStatusCode GetStatusCode(
        HttpContext httpContext,
        Exception exception)
        => exception is BadHttpRequestException badRequest &&
           badRequest.StatusCode is >= 400 and <= 499
            ? (HttpStatusCode)badRequest.StatusCode
            : base.GetStatusCode(httpContext, exception);
}
