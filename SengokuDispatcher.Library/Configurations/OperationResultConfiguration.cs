using Sengoku.Observability;
using SengokuDispatcher.Library.Services.Feeds;
using SengokuProvider.Library.Models.Leagues;

namespace SengokuDispatcher.Library.Configurations;

internal static class OperationResultConfiguration
{
    internal static OperationResultPolicy Create()
    {
        var policy = new OperationResultPolicy();
        policy.Register<PlayerOnboardResult>((scope, result) =>
        {
            scope.Add(OperationFields.Of(("WorkflowId", result.OperationId), ("Outcome", result.Status),
                ("ResultCount", result.Successful.Count), ("ErrorCount", result.Failures.Count)));
            if (result.Status is "Failed" or "Expired" || result.Failures.Count > 0) scope.Reject("BracketProcessingFailed");
            else if (result.Status == "Pending") scope.Pending();
        });
        policy.Register<TournamentOnboardResult>((scope, result) =>
        {
            scope.Add(OperationFields.Of(("ResultCount", result.Successful.Count), ("ErrorCount", result.Failures.Count)));
            if (result.Failures.Count > 0) scope.Reject("TournamentOnboardingFailed");
        });
        policy.Register<LeagueOnboardResult>((scope, result) =>
        {
            scope.Add(OperationFields.Of(("ResultCount", result.Successful.Count), ("ErrorCount", result.Failures.Count)));
            if (result.Failures.Count > 0) scope.Reject("LeagueOnboardingFailed");
        });
        policy.Register<DeliveryResult>((scope, result) =>
        {
            scope.Add(OperationFields.Of(("Outcome", result.Status), ("MessageId", result.MessageId)));
            if (result.Status == "Retry") scope.Pending();
            else if (result.Status != "Sent") scope.Reject("DeliveryNotConfirmed");
        });
        return policy;
    }
}
