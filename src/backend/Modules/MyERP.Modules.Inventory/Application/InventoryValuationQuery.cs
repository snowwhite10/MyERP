using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Application;

public sealed class InventoryValuationQuery(MyErpDbContext db) : IInventoryValuationQuery
{
    public async Task<InventoryValuation> GetItemAsync(long itemId, DateTime? cutoffPostedAt = null, CancellationToken token = default)
    {
        if (itemId <= 0) throw new BusinessRuleException("INV.VALUATION.INVALID_SCOPE", "Item id must be positive.");
        if (db.CurrentCompanyId is not long companyId || companyId <= 0)
            throw new BusinessRuleException("INV.VALUATION.INVALID_SCOPE", "A company is required.");

        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(token);
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_inv_value_item";
            command.CommandType = CommandType.StoredProcedure;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new SqlParameter("@company_id", SqlDbType.BigInt) { Value = companyId });
            command.Parameters.Add(new SqlParameter("@item_id", SqlDbType.BigInt) { Value = itemId });
            command.Parameters.Add(new SqlParameter("@cutoff_posted_at", SqlDbType.DateTime2)
                { Scale = 6, Value = cutoffPostedAt is DateTime cutoff ? cutoff : DBNull.Value });
            using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Valuation procedure returned no row.");
            return new InventoryValuation(itemId, cutoffPostedAt, reader.GetDecimal(0),
                reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2), reader.GetDecimal(3),
                reader.GetString(4), reader.GetInt64(5));
        }
        catch (SqlException ex) when (ex.Number is >= 51030 and <= 51033)
        {
            throw new BusinessRuleException(ex.Message.Split('\n')[0], "Inventory valuation failed.");
        }
        catch (SqlException ex) when (ex.Number == 8115)
        {
            throw new BusinessRuleException("INV.VALUATION.OVERFLOW", "Inventory valuation exceeds supported precision.");
        }
        finally
        {
            if (close) await connection.CloseAsync();
        }
    }
}
