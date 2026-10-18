using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Inventory.Infrastructure;

public sealed class StockMovementDbModel : IModuleDbModel
{
    public const string ImmutableTrigger = "trg_inv_stock_movement_immutable";

    public void Configure(ModelBuilder modelBuilder) => modelBuilder.Entity<StockMovement>(e =>
    {
        e.ToTable("inv_stock_movement", t =>
        {
            t.HasCheckConstraint("ck_inv_stock_movement_quantity", "[quantity] > 0");
            t.HasCheckConstraint("ck_inv_stock_movement_unit_cost", "[unit_cost] IS NULL OR ([unit_cost] >= 0 AND [direction] = 'In')");
            t.HasCheckConstraint("ck_inv_stock_movement_direction", "[direction] IN ('In', 'Out')");
            t.HasCheckConstraint("ck_inv_stock_movement_source", "[source_id] > 0 AND [source_line_no] > 0");
            t.HasTrigger(ImmutableTrigger);
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedOnAdd();
        e.Property(x => x.SourceType).HasMaxLength(30).IsUnicode(false);
        e.Property(x => x.Direction).HasMaxLength(10);
        e.Property(x => x.Quantity).HasPrecision(19, 6);
        e.Property(x => x.UnitCost).HasPrecision(19, 6);
        // 同公司複合外鍵：異動只能指向本公司的引用；每個引用只屬一行異動
        e.HasOne(x => x.Reference).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReferenceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.CompanyId, x.ReferenceId }).IsUnique();
        e.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.SourceLineNo, x.Direction }).IsUnique();
        // 結存彙總：依公司、物料、倉庫、儲位分組加總，覆蓋方向與數量避免回表
        e.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId, x.LocationId })
            .IncludeProperties(x => new { x.Direction, x.Quantity })
            .HasDatabaseName("ix_inv_stock_movement_balance");
        e.HasIndex(x => new { x.CompanyId, x.ItemId, x.PostedAt, x.Id })
            .IncludeProperties(x => new { x.Direction, x.Quantity, x.UnitCost, x.SourceType, x.SourceId, x.SourceLineNo })
            .HasDatabaseName("ix_inv_stock_movement_valuation");
        e.HasIndex(x => new { x.CompanyId, x.WarehouseId, x.LocationId });
        e.HasIndex(x => new { x.CompanyId, x.BusinessDate });
        // 進出存業務日期報表：日期範圍與分組鍵均由索引覆蓋，避免逐筆回表。
        e.HasIndex(x => new { x.CompanyId, x.BusinessDate, x.ItemId, x.WarehouseId, x.LocationId })
            .IncludeProperties(x => new { x.Direction, x.Quantity })
            .HasDatabaseName("ix_inv_stock_movement_flow_report");
    });
}
