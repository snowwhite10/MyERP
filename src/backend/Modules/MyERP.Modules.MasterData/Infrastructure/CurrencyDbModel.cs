using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Infrastructure;

/// <summary>幣別與匯率（ADR-037）；匯率以 (company_id, currency_id) 複合外鍵參照同公司幣別。</summary>
public sealed class CurrencyDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        MasterCategoryModel.Configure<Currency>(modelBuilder, "md_currency");
        modelBuilder.Entity<Currency>(e =>
        {
            e.ToTable("md_currency", t => t.HasCheckConstraint(
                "ck_md_currency_decimal_places", $"[decimal_places] BETWEEN 0 AND {Currency.MaxDecimalPlaces}"));
            e.Property(x => x.Code).HasMaxLength(Currency.CodeLength).IsFixedLength();
            e.Property(x => x.Symbol).HasMaxLength(Currency.SymbolMaxLength);
            e.HasAlternateKey(x => new { x.CompanyId, x.Id });
            e.HasIndex(x => x.CompanyId).IsUnique().HasFilter("[is_base] = 1 AND [is_deleted] = 0")
                .HasDatabaseName("ux_md_currency_company_id_base");
        });
        modelBuilder.Entity<ExchangeRate>(e =>
        {
            e.ToTable("md_exchange_rate", t =>
            {
                t.HasCheckConstraint("ck_md_exchange_rate_rate", "[rate] > 0");
                t.HasCheckConstraint("ck_md_exchange_rate_period", "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Rate).HasPrecision(ExchangeRate.RatePrecision, ExchangeRate.RateScale);
            e.HasIndex(x => new { x.CompanyId, x.CurrencyId, x.EffectiveFrom }).IsUnique().HasFilter("[is_deleted] = 0")
                .HasDatabaseName("ux_md_exchange_rate_company_id_currency_id_effective_from");
            // 篩選索引不能支援外鍵檢查，另建完整的外鍵索引。
            e.HasIndex(x => new { x.CompanyId, x.CurrencyId }).HasDatabaseName("ix_md_exchange_rate_company_id_currency_id");
            e.HasOne(x => x.Currency).WithMany()
                .HasForeignKey(x => new { x.CompanyId, x.CurrencyId })
                .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
