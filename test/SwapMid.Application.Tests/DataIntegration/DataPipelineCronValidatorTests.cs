using Shouldly;
using Volo.Abp;
using Xunit;

namespace SwapMid.DataIntegration;

public class DataPipelineCronValidatorTests
{
    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 2 * * *")]
    [InlineData("30 1 * * 1-5")]
    public void Valid_Five_Field_Cron_Should_Pass_And_Trim(string cron)
    {
        var normalized = DataPipelineCronValidator.ValidateAndNormalize(true, $"  {cron}  ");
        normalized.ShouldBe(cron);
    }

    [Fact]
    public void Disabled_Schedule_With_Blank_Cron_Should_Return_Null()
    {
        DataPipelineCronValidator.ValidateAndNormalize(false, "   ").ShouldBeNull();
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "invalid cron")]
    [InlineData(true, "0 0 12 * * ?")] // 6-field Quartz cron rejected in 5-field standard mode
    [InlineData(false, "not-a-cron")]
    public void Invalid_Or_Missing_Cron_When_Enabled_Should_Throw_Domain_Error(bool enabled, string? cron)
    {
        var ex = Should.Throw<BusinessException>(
            () => DataPipelineCronValidator.ValidateAndNormalize(enabled, cron));
        ex.Code.ShouldBe(SwapMidDomainErrorCodes.DataIntegration.PipelineScheduleCronInvalid);
    }
}
