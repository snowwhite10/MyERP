using MyERP.Shared.Domain;

namespace MyERP.Modules.Sales.Domain;

/// <summary>Order decision point that recorded a credit override (BR-SD-034).</summary>
public enum SalesCreditDecisionStage { Submit, FinalApproval }

/// <summary>
/// WU-SD-006b1 (BR-SD-034/036, REQ_DS43.205): append-only evidence of one reasoned credit-limit override, bound to
/// the order, the order version it was decided on and the SYS approval round. Actor and time are the standard
/// <c>created_by</c>/<c>created_at</c> stamped from the server user and <c>IClock</c>; the credit components are the
/// FI snapshot and SD commitment figures measured under the FI credit lock. A database trigger refuses UPDATE and
/// DELETE, and a unique key allows one row per order, round and stage.
/// </summary>
public sealed class SalesCreditOverride : BusinessEntity
{
    public long OrderId { get; set; }
    public int OrderVersion { get; set; }
    public long ApprovalInstanceId { get; set; }
    public SalesCreditDecisionStage Stage { get; set; }
    public DateOnly EvaluationDate { get; set; }
    public int CreditLimitVersion { get; set; }
    public required string BaseCurrencyCode { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal PostedOpenAr { get; set; }
    public decimal OtherCommitments { get; set; }
    public decimal Candidate { get; set; }
    public decimal Excess { get; set; }
    public required string Reason { get; set; }
}
