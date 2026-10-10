namespace MyERP.Shared.Domain;

/// <summary>審計欄位（created_at / created_by / updated_at / updated_by），由 SaveChanges interceptor 填入，業務程式不得設定。</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }

    long CreatedBy { get; set; }

    DateTime? UpdatedAt { get; set; }

    long? UpdatedBy { get; set; }
}

/// <summary>多公司隔離（company_id）：EF Global Query Filter 只回傳目前公司的資料；新增時由 interceptor 填入目前公司。</summary>
public interface ICompanyOwned
{
    long CompanyId { get; set; }
}

/// <summary>樂觀鎖（version）：EF concurrency token，新增為 1，每次修改由 interceptor 遞增。</summary>
public interface IVersioned
{
    int Version { get; set; }
}

/// <summary>軟刪除（is_deleted，主檔）：Global Query Filter 排除已刪資料。</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}

/// <summary>主鍵 <c>id BIGINT IDENTITY</c>。</summary>
public abstract class Entity
{
    public long Id { get; set; }
}

/// <summary>業務表基底：具備 MASTER_PROMPT 6.4 全部必備欄位。</summary>
public abstract class BusinessEntity : Entity, IAuditable, ICompanyOwned, IVersioned
{
    public long CompanyId { get; set; }

    public DateTime CreatedAt { get; set; }

    public long CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public long? UpdatedBy { get; set; }

    public int Version { get; set; }
}

/// <summary>主檔基底：另有 is_active（停用）與 is_deleted（軟刪除）。被單據引用後只能停用，不能刪除。</summary>
public abstract class MasterEntity : BusinessEntity, ISoftDeletable
{
    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }
}
