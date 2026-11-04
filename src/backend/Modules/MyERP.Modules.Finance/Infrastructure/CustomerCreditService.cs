using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MyERP.Modules.Finance.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// FI credit owner (BR-FI-003/006). Works on the caller's open SQL transaction: the pair application lock and the
/// permanent key row lock are owned by that transaction and released only by its commit or rollback.
/// The pair applock alone serializes find-or-insert of the key, so the row read uses UPDLOCK without HOLDLOCK:
/// a key-range lock on an unseen pair would also block neighbouring customers (FI-CREDIT-021). Callers use
/// READ COMMITTED; SERIALIZABLE callers would reintroduce range locks.
/// </summary>
internal sealed class CustomerCreditService(IClock clock, ICurrentUser currentUser) : ICustomerCreditService
{
    private const int LockNotGranted = 51010;
    private const int SqlLockTimeout = 1222;
    private const int LockNotHeld = 51011;

    /// <summary>Maximum wait for another transaction holding the same pair; tests shorten it.</summary>
    internal int LockTimeoutMilliseconds { get; init; } = 15_000;

    internal static string LockResource(long companyId, long customerId) =>
        string.Create(CultureInfo.InvariantCulture, $"ar_credit:{companyId}:{customerId}");

    public async Task AcquireCreditLockAsync(long companyId, long customerId, DbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var connection = Validate(companyId, customerId, transaction);
        await using var command = Command(connection, transaction, """
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;
            IF @result < 0 THROW 51010, 'FI credit lock was not granted.', 1;
            DECLARE @id bigint = (SELECT id FROM dbo.ar_credit_key WITH (UPDLOCK, ROWLOCK)
                                  WHERE company_id = @company AND customer_id = @customer);
            IF @id IS NULL
            BEGIN
                INSERT dbo.ar_credit_key (company_id, customer_id, created_at, created_by, version)
                VALUES (@company, @customer, @now, @user, 1);
                SET @id = SCOPE_IDENTITY();
            END;
            SELECT @id;
            """);
        Add(command, "@timeout", SqlDbType.Int, LockTimeoutMilliseconds);
        Add(command, "@resource", SqlDbType.NVarChar, LockResource(companyId, customerId), 255);
        Add(command, "@company", SqlDbType.BigInt, companyId);
        Add(command, "@customer", SqlDbType.BigInt, customerId);
        Add(command, "@now", SqlDbType.DateTime2, clock.LocalNow);
        Add(command, "@user", SqlDbType.BigInt, currentUser.UserId ?? SystemUser.Id);
        try
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is not long)
                throw new InvalidOperationException("FI credit key was not established.");
        }
        catch (SqlException ex) when (ex.Number is LockNotGranted or SqlLockTimeout)
        {
            throw new ConflictException(FinanceModuleContracts.CreditLockTimeout,
                "Customer credit is being updated by another transaction. Please retry.");
        }
    }

    public async Task<CreditSnapshot> GetSnapshotAsync(long companyId, long customerId, DateOnly evaluationDate,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        var connection = Validate(companyId, customerId, transaction);
        ArgumentOutOfRangeException.ThrowIfEqual(evaluationDate, default);
        await using var command = Command(connection, transaction, """
            IF APPLOCK_MODE('public', @resource, 'Transaction') <> 'Exclusive'
                THROW 51011, 'FI credit lock must be acquired in this transaction before reading credit.', 1;
            SELECT base_currency, amount, version FROM dbo.ar_credit_limit
            WHERE company_id = @company AND customer_id = @customer AND status = 'Active'
              AND effective_from <= @date AND effective_to >= @date;
            SELECT base_currency,
                   CASE WHEN SUM(open_base_amount) > 999999999999999.9999 THEN CAST(-1 AS decimal(19,4))
                        ELSE CAST(SUM(open_base_amount) AS decimal(19,4)) END
            FROM dbo.ar_posted_receivable
            WHERE company_id = @company AND customer_id = @customer AND open_base_amount > 0
            GROUP BY base_currency;
            """);
        Add(command, "@resource", SqlDbType.NVarChar, LockResource(companyId, customerId), 255);
        Add(command, "@company", SqlDbType.BigInt, companyId);
        Add(command, "@customer", SqlDbType.BigInt, customerId);
        Add(command, "@date", SqlDbType.Date, evaluationDate);

        var limits = new List<(string Currency, decimal Amount, int Version)>();
        var exposure = new List<(string Currency, decimal Amount)>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                limits.Add((reader.GetString(0), reader.GetDecimal(1), reader.GetInt32(2)));
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                exposure.Add((reader.GetString(0), reader.GetDecimal(1)));
        }
        catch (SqlException ex) when (ex.Number == LockNotHeld)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        if (limits.Count == 0) return Unavailable(CreditUnavailableReason.MissingLimit);
        if (limits.Count > 1) return Unavailable(CreditUnavailableReason.AmbiguousLimit);
        var limit = limits[0];
        if (exposure.Any(x => x.Currency != limit.Currency || x.Amount < 0m))
            return Unavailable(CreditUnavailableReason.ExposureUnavailable);
        var open = exposure.Sum(x => x.Amount);
        return CreditSnapshot.Available(companyId, customerId, limit.Currency, limit.Amount, open, evaluationDate,
            limit.Version, clock.LocalNow);

        CreditSnapshot Unavailable(CreditUnavailableReason reason) =>
            CreditSnapshot.Unavailable(companyId, customerId, evaluationDate, reason);
    }

    private static SqlConnection Validate(long companyId, long customerId, DbTransaction transaction)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(companyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.Connection as SqlConnection
            ?? throw new ArgumentException("Credit requires an open SQL Server transaction.", nameof(transaction));
    }

    private static SqlCommand Command(SqlConnection connection, DbTransaction transaction, string sql) =>
        new(sql, connection, (SqlTransaction)transaction);

    private static void Add(SqlCommand command, string name, SqlDbType type, object value, int size = 0)
    {
        var parameter = command.Parameters.Add(name, type);
        if (size > 0) parameter.Size = size;
        parameter.Value = value;
    }
}
