using System.Net;
using System.Threading.Tasks;
using SwapMid.HttpApiHost.Scheduling;
using Shouldly;
using Volo.Abp;
using Volo.Abp.AspNetCore.ExceptionHandling;

namespace SwapMid.Scheduling;

public class JobSchedulingInputContractTests
{
    [Fact]
    public void 任务参数过长必须映射为HTTP400()
    {
        var options = new AbpExceptionHttpStatusCodeOptions();

        SwapMidHttpApiHostModule
            .ConfigureSchedulingStatusCodes(options);

        options.ErrorCodeToHttpStatusCodeMappings[
                SwapMidDomainErrorCodes.Scheduling
                    .JobArgumentsTooLong]
            .ShouldBe(HttpStatusCode.BadRequest);
        options.ErrorCodeToHttpStatusCodeMappings.Count.ShouldBe(1);
    }

    [Fact]
    public async Task 任务参数过长必须在解析或入队前返回稳定业务错误()
    {
        var service = new JobSchedulingAppService(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);

        var exception = await Should.ThrowAsync<BusinessException>(() =>
            service.RunAsync(new RunScheduledJobInput
            {
                Key = "unused",
                ArgsJson = new string(
                    'x',
                    JobRunContext.MaxArgsJsonCharacters + 1),
            }));

        exception.Code.ShouldBe(
            SwapMidDomainErrorCodes.Scheduling
                .JobArgumentsTooLong);
        exception.Data["max"].ShouldBe(
            JobRunContext.MaxArgsJsonCharacters);
    }
}
