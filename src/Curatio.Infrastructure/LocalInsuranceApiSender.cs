using Curatio.Core;

namespace Curatio.Infrastructure;

public sealed class LocalInsuranceApiSender : IInsuranceApiSender
{
    public Task<ApiSendResult> SendAsync(InsuranceRecord record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ApiSendResult(
            false,
            ErrorMessage: "API не настроен. Данные не отправлялись."));
    }
}
