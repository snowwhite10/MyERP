namespace MyERP.Modules.Finance.Contracts;

/// <summary>FI cross-module contract namespace.</summary>
public static class FinanceModuleContracts
{
    public const string ModuleCode = "FI";

    /// <summary>409, retryable: another transaction holds the company/customer credit key beyond the lock timeout.</summary>
    public const string CreditLockTimeout = "FI.CREDIT.LOCK_TIMEOUT";
}
