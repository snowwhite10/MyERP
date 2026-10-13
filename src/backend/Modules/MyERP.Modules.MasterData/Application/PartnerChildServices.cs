using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>客戶聯絡人：姓名必填，電話、電郵等選填；最多一位預設聯絡人（ADR-043）。</summary>
public sealed class CustomerContactService(MyErpDbContext db)
    : PartnerChildService<Customer, CustomerContact>(db, "md_customer_contact", "MD.CUSTOMER_CONTACT", "Customer contact",
        "MD.CUSTOMER", "Customer")
{
    public Task<CustomerContact> CreateAsync(long customerId, ContactInput input, CancellationToken token = default)
    {
        var details = ContactDetails.Validate(input);
        return CreateCoreAsync(customerId, input.IsDefault,
            (companyId, partnerId) => details.ApplyTo(new CustomerContact { CompanyId = companyId, PartnerId = partnerId, Name = details.Name }),
            token);
    }

    public Task<CustomerContact> UpdateAsync(long customerId, long id, ContactInput input, bool isActive, int version,
        CancellationToken token = default)
    {
        var details = ContactDetails.Validate(input);
        return UpdateCoreAsync(customerId, id, input.IsDefault, isActive, version, x => details.ApplyTo(x), token);
    }
}

/// <summary>客戶送貨地址：地址必填，郵遞區號、收件人與電話選填；最多一筆預設地址（ADR-043）。</summary>
public sealed class CustomerAddressService(MyErpDbContext db)
    : PartnerChildService<Customer, CustomerAddress>(db, "md_customer_address", "MD.CUSTOMER_ADDRESS", "Customer address",
        "MD.CUSTOMER", "Customer")
{
    public Task<CustomerAddress> CreateAsync(long customerId, AddressInput input, CancellationToken token = default)
    {
        var details = AddressDetails.Validate(input);
        return CreateCoreAsync(customerId, input.IsDefault,
            (companyId, partnerId) => details.ApplyTo(new CustomerAddress { CompanyId = companyId, PartnerId = partnerId, Address = details.Address }),
            token);
    }

    public Task<CustomerAddress> UpdateAsync(long customerId, long id, AddressInput input, bool isActive, int version,
        CancellationToken token = default)
    {
        var details = AddressDetails.Validate(input);
        return UpdateCoreAsync(customerId, id, input.IsDefault, isActive, version, x => details.ApplyTo(x), token);
    }
}

/// <summary>供應商聯絡人：欄位與規則同客戶聯絡人（ADR-043）。</summary>
public sealed class SupplierContactService(MyErpDbContext db)
    : PartnerChildService<Supplier, SupplierContact>(db, "md_supplier_contact", "MD.SUPPLIER_CONTACT", "Supplier contact",
        "MD.SUPPLIER", "Supplier")
{
    public Task<SupplierContact> CreateAsync(long supplierId, ContactInput input, CancellationToken token = default)
    {
        var details = ContactDetails.Validate(input);
        return CreateCoreAsync(supplierId, input.IsDefault,
            (companyId, partnerId) => details.ApplyTo(new SupplierContact { CompanyId = companyId, PartnerId = partnerId, Name = details.Name }),
            token);
    }

    public Task<SupplierContact> UpdateAsync(long supplierId, long id, ContactInput input, bool isActive, int version,
        CancellationToken token = default)
    {
        var details = ContactDetails.Validate(input);
        return UpdateCoreAsync(supplierId, id, input.IsDefault, isActive, version, x => details.ApplyTo(x), token);
    }
}

public sealed record ContactInput(string? Name, string? Position = null, string? Mobile = null, string? Phone = null,
    string? Email = null, string? MessagingId = null, string? Note = null, bool IsDefault = false);

public sealed record AddressInput(string? Address, string? PostalCode = null, string? Recipient = null, string? Phone = null,
    bool IsDefault = false);

internal sealed record ContactDetails(string Name, string? Position, string? Mobile, string? Phone, string? Email,
    string? MessagingId, string? Note)
{
    public static ContactDetails Validate(ContactInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > PartnerContact.NameMaxLength)
            throw new RequestValidationException("name", $"Required; maximum {PartnerContact.NameMaxLength} characters.");
        var email = MasterDataInput.OptionalText(input.Email, "email", PartnerContact.EmailMaxLength);
        if (email is not null && !(MailAddress.TryCreate(email, out var parsed) && parsed.Address == email))
            throw new RequestValidationException("email", "Enter a single valid e-mail address.");
        return new(name,
            MasterDataInput.OptionalText(input.Position, "position", PartnerContact.PositionMaxLength),
            MasterDataInput.OptionalText(input.Mobile, "mobile", PartnerContact.PhoneMaxLength),
            MasterDataInput.OptionalText(input.Phone, "phone", PartnerContact.PhoneMaxLength),
            email,
            MasterDataInput.OptionalText(input.MessagingId, "messagingId", PartnerContact.MessagingIdMaxLength),
            MasterDataInput.OptionalText(input.Note, "note", PartnerContact.NoteMaxLength));
    }

    public T ApplyTo<T>(T entity) where T : PartnerContact
    {
        (entity.Name, entity.Position, entity.Mobile, entity.Phone, entity.Email, entity.MessagingId, entity.Note) =
            (Name, Position, Mobile, Phone, Email, MessagingId, Note);
        return entity;
    }
}

internal sealed record AddressDetails(string Address, string? PostalCode, string? Recipient, string? Phone)
{
    public static AddressDetails Validate(AddressInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var address = input.Address?.Trim();
        if (string.IsNullOrEmpty(address) || address.Length > CustomerAddress.AddressMaxLength)
            throw new RequestValidationException("address", $"Required; maximum {CustomerAddress.AddressMaxLength} characters.");
        return new(address,
            MasterDataInput.OptionalText(input.PostalCode, "postalCode", CustomerAddress.PostalCodeMaxLength),
            MasterDataInput.OptionalText(input.Recipient, "recipient", CustomerAddress.RecipientMaxLength),
            MasterDataInput.OptionalText(input.Phone, "phone", CustomerAddress.PhoneMaxLength));
    }

    public CustomerAddress ApplyTo(CustomerAddress entity)
    {
        (entity.Address, entity.PostalCode, entity.Recipient, entity.Phone) = (Address, PostalCode, Recipient, Phone);
        return entity;
    }
}

/// <summary>
/// 往來對象子資料的共用維護（ADR-043）：往來對象須在目前公司且未刪除；設為預設時先取消同一往來對象的其他預設，
/// 以公司級 applock 序列化，篩選唯一索引為最後防線；停用或刪除的資料不可為預設。
/// </summary>
public abstract class PartnerChildService<TPartner, TChild>(MyErpDbContext db, string lockName, string errorPrefix,
    string resourceName, string partnerErrorPrefix, string partnerName)
    where TPartner : MasterEntity
    where TChild : PartnerChild
{
    /// <summary>列出往來對象下未刪除的資料（含停用）；預設排第一，其餘按建立順序。</summary>
    public async Task<IReadOnlyList<TChild>> ListAsync(long partnerId, CancellationToken token = default)
    {
        RequireCompany();
        await EnsurePartnerAsync(partnerId, token);
        return await db.Set<TChild>().AsNoTracking().Where(x => x.PartnerId == partnerId)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Id).ToListAsync(token);
    }

    public Task DeleteAsync(long partnerId, long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(db, lockName, RequireCompany(), async () =>
        {
            var entity = await FindAsync(partnerId, id, version, token);
            entity.IsDefault = false;
            entity.IsDeleted = true;
            await SaveAsync(entity, version, token);
            return true;
        }, token);

    protected Task<TChild> CreateCoreAsync(long partnerId, bool isDefault, Func<long, long, TChild> create, CancellationToken token)
    {
        var companyId = RequireCompany();
        return CompanyLock.RunAsync(db, lockName, companyId, async () =>
        {
            await EnsurePartnerAsync(partnerId, token);
            if (isDefault) await ClearDefaultAsync(partnerId, null, token);
            var entity = create(companyId, partnerId);
            entity.IsDefault = isDefault;
            db.Add(entity);
            await db.SaveChangesAsync(token);
            return entity;
        }, token);
    }

    protected Task<TChild> UpdateCoreAsync(long partnerId, long id, bool isDefault, bool isActive, int version,
        Action<TChild> apply, CancellationToken token)
    {
        if (isDefault && !isActive)
            throw new RequestValidationException("isDefault", "An inactive record cannot be the default.");
        return CompanyLock.RunAsync(db, lockName, RequireCompany(), async () =>
        {
            var entity = await FindAsync(partnerId, id, version, token);
            if (isDefault && !entity.IsDefault) await ClearDefaultAsync(partnerId, id, token);
            apply(entity);
            entity.IsDefault = isDefault;
            entity.IsActive = isActive;
            await SaveAsync(entity, version, token);
            return entity;
        }, token);
    }

    /// <summary>先單獨保存取消預設，避免同一批更新觸發篩選唯一索引。</summary>
    private async Task ClearDefaultAsync(long partnerId, long? exceptId, CancellationToken token)
    {
        var current = await db.Set<TChild>().Where(x => x.PartnerId == partnerId && x.IsDefault && x.Id != exceptId).ToListAsync(token);
        if (current.Count == 0) return;
        foreach (var row in current) row.IsDefault = false;
        await db.SaveChangesAsync(token);
    }

    private async Task EnsurePartnerAsync(long partnerId, CancellationToken token)
    {
        if (!await db.Set<TPartner>().AnyAsync(x => x.Id == partnerId, token))
            throw new NotFoundException(partnerErrorPrefix + ".NOT_FOUND", partnerName, partnerId);
    }

    private async Task<TChild> FindAsync(long partnerId, long id, int version, CancellationToken token)
    {
        await EnsurePartnerAsync(partnerId, token);
        var entity = await db.Set<TChild>().SingleOrDefaultAsync(x => x.Id == id && x.PartnerId == partnerId, token)
            ?? throw new NotFoundException(errorPrefix + ".NOT_FOUND", resourceName, id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        return entity;
    }

    private async Task SaveAsync(TChild entity, int version, CancellationToken token)
    {
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
    }

    private long RequireCompany() => MasterDataQueries.RequireCompany(db);
}
