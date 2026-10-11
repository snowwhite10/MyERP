using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Settings;
using Keys = MyERP.Modules.System.Application.Parameters.SystemParameterDefinitions;

namespace MyERP.Modules.System.Application.Parameters;

/// <summary><see cref="IPrintSettingsReader"/> 實作：讀取 <c>print</c> 分組參數，公司名稱空白時用公司主檔名稱（ADR-027）。</summary>
public sealed class PrintSettingsReader(MyErpDbContext db, ISystemParameters parameters) : IPrintSettingsReader
{
    public async Task<PrintSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var companyId = db.CurrentCompanyId ?? throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        var companyName = await parameters.GetAsync(Keys.PrintCompanyName, cancellationToken);
        if (companyName.Length == 0)
        {
            companyName = await db.Set<SystemCompany>().AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken)
                ?? throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");
        }

        return new PrintSettings(
            companyName,
            await parameters.GetAsync(Keys.PrintAddress, cancellationToken),
            await parameters.GetAsync(Keys.PrintPostalCode, cancellationToken),
            await parameters.GetAsync(Keys.PrintPhone, cancellationToken),
            await parameters.GetAsync(Keys.PrintFax, cancellationToken),
            await parameters.GetAsync(Keys.PrintPurchaseOrderFooter, cancellationToken),
            await parameters.GetAsync(Keys.PrintSalesDeliveryFooter, cancellationToken),
            await parameters.GetBooleanAsync(Keys.PrintShowLogo, cancellationToken),
            await parameters.GetBooleanAsync(Keys.PrintShowStamp, cancellationToken));
    }
}
