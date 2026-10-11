using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.CompanyImages;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-IMG-001～006：公司 Logo／印章的內容檢查、上傳／替換／刪除 API、權限、公司隔離與 ICompanyImageReader（ADR-028）。</summary>
public sealed class CompanyImageTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Path = "/api/v1/sys/company-images";
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public void InspectorDetectsPngAndJpegByContent()
    {
        Assert.Equal(new ImageInfo("image/png", 300, 120), ImageInspector.Inspect(Png(300, 120)));
        Assert.Equal(new ImageInfo("image/jpeg", 640, 480), ImageInspector.Inspect(Jpeg(640, 480)));
        // SOF2（漸進式）與段落前的填充 FF
        Assert.Equal(new ImageInfo("image/jpeg", 32, 64), ImageInspector.Inspect(Jpeg(32, 64, sof: 0xC2, fill: true)));
        // 寬高超出 int 範圍時視為極大值，交由尺寸檢查拒絕
        Assert.Equal(int.MaxValue, ImageInspector.Inspect(Png(0xFFFF_FFFF, 20))!.Width);
    }

    [Fact]
    public void InspectorRejectsOtherOrBrokenFiles()
    {
        var png = Png(100, 100);
        Assert.Null(ImageInspector.Inspect([]));
        Assert.Null(ImageInspector.Inspect("GIF89a\x01\x00\x01\x00"u8.ToArray()));
        Assert.Null(ImageInspector.Inspect(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")));
        Assert.Null(ImageInspector.Inspect(png.AsSpan(0, png.Length - 12))); // 沒有 IEND（截斷）
        var wrongHeader = (byte[])png.Clone();
        wrongHeader[12] = (byte)'X'; // 第一個 chunk 不是 IHDR
        Assert.Null(ImageInspector.Inspect(wrongHeader));
        var jpeg = Jpeg(100, 100);
        Assert.Null(ImageInspector.Inspect(jpeg.AsSpan(0, jpeg.Length - 2))); // 沒有 EOI
        Assert.Null(ImageInspector.Inspect([0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9])); // SOF 之前就是影像資料
        Assert.Null(ImageInspector.Inspect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x40, 0xFF, 0xD9])); // 段落長度超出檔案
        Assert.Null(ImageInspector.Inspect([0xFF, 0xD8, 0x00, 0xFF, 0xD9]));
    }

    [Theory]
    [InlineData("logo.png", "logo.png")]
    [InlineData(@"C:\fakepath\..\公司 印章.JPG", "公司 印章.JPG")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("bad\u0000na\u001fme.png", "badname.png")]
    [InlineData("   ", "stamp.png")]
    [InlineData(null, "stamp.png")]
    public void FileNamesAreReducedToSafeNames(string? input, string expected) =>
        Assert.Equal(expected, CompanyImageService.SafeFileName(input, CompanyImageKind.Stamp, "image/png"));

    [Fact]
    public void LongFileNamesAreTruncated() =>
        Assert.Equal(200, CompanyImageService.SafeFileName(new string('a', 300) + ".png", CompanyImageKind.Logo, "image/png").Length);

    [Fact]
    public async Task ImagesAreUploadedReplacedDownloadedAndDeletedWithVersions()
    {
        var company = await CreateCompanyAsync("Image Co");
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, company, await CreateRoleAsync(true));
        using var client = factory.CreateClient();

        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, token, company))).EnumerateArray());

        // 類型以內容判斷：檔名與宣告的 Content-Type 都說是 GIF，實際是 PNG
        var png = Png(240, 80);
        var uploaded = await UploadAsync(client, token, company, "logo", png, @"C:\fakepath\logo.gif", "image/gif", 0);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var logo = await ReadAsync(uploaded);
        Assert.Equal(("Logo", "image/png", "logo.gif", png.Length, 240, 80, 1),
            (logo.GetProperty("kind").GetString(), logo.GetProperty("contentType").GetString(), logo.GetProperty("fileName").GetString(),
             logo.GetProperty("sizeBytes").GetInt32(), logo.GetProperty("width").GetInt32(), logo.GetProperty("height").GetInt32(), logo.GetProperty("version").GetInt32()));

        // 已有圖片時以 version 0 再上傳（另一人先上傳）→ 409
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(client, token, company, "LOGO", png, "again.png", "image/png", 0)).StatusCode);

        var content = await SendAsync(client, HttpMethod.Get, $"{Path}/logo/content", token, company);
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("image/png", content.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await content.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("inline", content.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", content.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("\"1\"", content.Headers.ETag!.Tag);
        Assert.Contains("no-cache", content.Headers.CacheControl!.ToString(), StringComparison.Ordinal);

        var notModified = new HttpRequestMessage(HttpMethod.Get, $"{Path}/logo/content");
        Authorize(notModified, token, company);
        notModified.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"1\""));
        var cached = await client.SendAsync(notModified, Ct);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        Assert.Equal("\"1\"", cached.Headers.ETag!.Tag);
        // 舊版本的 ETag 不相符：回傳完整內容
        var stale = new HttpRequestMessage(HttpMethod.Get, $"{Path}/logo/content");
        Authorize(stale, token, company);
        stale.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"0\""));
        var refreshed = await client.SendAsync(stale, Ct);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(png, await refreshed.Content.ReadAsByteArrayAsync(Ct));

        // 替換成 JPEG：版本遞增，舊版本再替換或刪除 → 409
        var jpeg = Jpeg(400, 400);
        var replaced = await ReadAsync(await UploadAsync(client, token, company, "logo", jpeg, "new-logo.jpg", "application/octet-stream", 1));
        Assert.Equal(("image/jpeg", 2, 400), (replaced.GetProperty("contentType").GetString(), replaced.GetProperty("version").GetInt32(), replaced.GetProperty("width").GetInt32()));
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(client, token, company, "logo", png, "old.png", "image/png", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Delete, $"{Path}/logo?version=1", token, company)).StatusCode);

        var stamp = await UploadAsync(client, token, company, "stamp", Png(120, 120), "stamp.png", "image/png", 0);
        Assert.Equal(HttpStatusCode.OK, stamp.StatusCode);
        var list = (await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, token, company))).EnumerateArray().ToList();
        Assert.Equal(["Logo", "Stamp"], list.Select(x => x.GetProperty("kind").GetString()));
        Assert.All(list, x => Assert.False(x.TryGetProperty("content", out _)));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"{Path}/logo?version=2", token, company)).StatusCode);
        var missing = await SendAsync(client, HttpMethod.Get, $"{Path}/logo/content", token, company);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(CompanyImageService.NotFoundCode, (await ReadAsync(missing)).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Delete, $"{Path}/logo?version=2", token, company)).StatusCode);
        Assert.Equal(["Stamp"], (await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, token, company))).EnumerateArray().Select(x => x.GetProperty("kind").GetString()));

        // 稽核：新增、替換、刪除 Logo 與新增印章，內容只記錄位元組數
        await using var db = database.CreateRawContext();
        var actions = await db.Database.SqlQuery<string>(
            $"SELECT action AS [Value] FROM sys_audit_log WHERE table_name = 'sys_company_image' AND company_id = {company} ORDER BY id").ToListAsync(Ct);
        Assert.Equal(["CREATE", "UPDATE", "CREATE", "DELETE"], actions);
        var changes = await db.Database.SqlQuery<string>(
            $"SELECT TOP 1 changes AS [Value] FROM sys_audit_log WHERE table_name = 'sys_company_image' AND company_id = {company} AND action = 'UPDATE'").SingleAsync(Ct);
        Assert.Contains($"(binary {jpeg.Length} bytes)", changes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidUploadsAreRejectedWithSpecificErrors()
    {
        var company = await CreateCompanyAsync("Reject Co");
        var (userId, token) = await CreateSignedInUserAsync();
        await AssignAsync(userId, company, await CreateRoleAsync(true));
        using var client = factory.CreateClient();

        async Task ExpectAsync(byte[] bytes, string fileName, HttpStatusCode status, string? errorCode)
        {
            var response = await UploadAsync(client, token, company, "stamp", bytes, fileName, "image/png", 0);
            Assert.Equal(status, response.StatusCode);
            if (errorCode is not null) Assert.Equal(errorCode, (await ReadAsync(response)).GetProperty("errorCode").GetString());
        }

        await ExpectAsync(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"), "stamp.png", HttpStatusCode.UnprocessableEntity, CompanyImageService.UnsupportedTypeCode);
        await ExpectAsync("GIF89a\x10\x00\x10\x00"u8.ToArray(), "stamp.png", HttpStatusCode.UnprocessableEntity, CompanyImageService.UnsupportedTypeCode);
        await ExpectAsync(Png(15, 100), "small.png", HttpStatusCode.UnprocessableEntity, CompanyImageService.InvalidDimensionsCode);
        await ExpectAsync(Png(100, 2049), "tall.png", HttpStatusCode.UnprocessableEntity, CompanyImageService.InvalidDimensionsCode);
        await ExpectAsync(Png(100, 100, padding: CompanyImageService.MaxBytes), "big.png", HttpStatusCode.UnprocessableEntity, CompanyImageService.TooLargeCode);
        await ExpectAsync([], "empty.png", HttpStatusCode.BadRequest, null);

        // 邊界：16 與 2048 像素、剛好 1 MB 可以上傳
        var exact = Png(16, 2048, padding: 0);
        exact = Png(16, 2048, padding: CompanyImageService.MaxBytes - exact.Length);
        Assert.Equal(CompanyImageService.MaxBytes, exact.Length);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, token, company, "stamp", exact, "exact.png", "image/png", 0)).StatusCode);

        var noFile = new HttpRequestMessage(HttpMethod.Put, $"{Path}/logo") { Content = new MultipartFormDataContent { { new StringContent("0"), "version" } } };
        Authorize(noFile, token, company);
        var missing = await client.SendAsync(noFile, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.True((await ReadAsync(missing)).GetProperty("errors").TryGetProperty("file", out _));

        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(client, token, company, "signature", Png(100, 100), "x.png", "image/png", 0)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"{Path}/0/content", token, company)).StatusCode);
    }

    [Fact]
    public async Task ViewOnlyUsersCannotEditAndCompaniesAreIsolated()
    {
        var companyA = await CreateCompanyAsync("Image A");
        var companyB = await CreateCompanyAsync("Image B");
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var admin = await CreateRoleAsync(true);
        await AssignAsync(adminId, companyA, admin);
        await AssignAsync(adminId, companyB, admin);
        var (viewerId, viewerToken) = await CreateSignedInUserAsync();
        await AssignAsync(viewerId, companyA, await CreateRoleAsync(false, SystemPermissions.ParameterView));
        var (otherId, otherToken) = await CreateSignedInUserAsync();
        await AssignAsync(otherId, companyA, await CreateRoleAsync(false, SystemPermissions.UserView));
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, adminToken, companyA, "logo", Png(64, 64), "a.png", "image/png", 0)).StatusCode);

        Assert.Single((await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, viewerToken, companyA))).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, $"{Path}/logo/content", viewerToken, companyA)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(client, viewerToken, companyA, "logo", Png(64, 64), "b.png", "image/png", 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Delete, $"{Path}/logo?version=1", viewerToken, companyA)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, Path, otherToken, companyA)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, $"{Path}/logo/content", null, companyA)).StatusCode);

        // 公司 B 看不到公司 A 的圖片，也可以各自上傳
        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, adminToken, companyB))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"{Path}/logo/content", adminToken, companyB)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(client, adminToken, companyB, "logo", Jpeg(64, 32), "b.jpg", "image/jpeg", 0)).StatusCode);
        Assert.Equal("image/png", (await SendAsync(client, HttpMethod.Get, $"{Path}/logo/content", adminToken, companyA)).Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task ReaderReturnsCurrentCompanyImageOrNull()
    {
        var company = await CreateCompanyAsync("Reader Co");
        var png = Png(50, 40);

        await using var scope = factory.Services.CreateAsyncScope();
        var registered = scope.ServiceProvider.GetRequiredService<ICompanyImageReader>();
        await Assert.ThrowsAsync<ForbiddenException>(() => registered.GetAsync(CompanyImageKind.Logo, Ct));

        await using var context = new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
            [new SystemOrganizationDbModel()], new FixedCompany(company));
        var reader = new CompanyImageReader(context);
        Assert.Null(await reader.GetAsync(CompanyImageKind.Logo, Ct));

        await using (var db = database.CreateRawContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO sys_company_image (company_id, image_kind, content_type, file_name, size_bytes, width, height, content, created_at, created_by, version) VALUES ({company}, 'Logo', 'image/png', 'logo.png', {png.Length}, 50, 40, {png}, SYSDATETIME(), 0, 1)", Ct);
        }

        var image = await reader.GetAsync(CompanyImageKind.Logo, Ct);
        Assert.NotNull(image);
        Assert.Equal((CompanyImageKind.Logo, "image/png", 50, 40), (image.Kind, image.ContentType, image.Width, image.Height));
        Assert.Equal(png, image.Content);
        Assert.Null(await reader.GetAsync(CompanyImageKind.Stamp, Ct));
    }

    /// <summary>最小 PNG：簽章、IHDR、可選的 tEXt 填充（把檔案補到指定大小附近）、IEND。CRC 不檢查，以 0 填入。</summary>
    internal static byte[] Png(uint width, uint height, int padding = 0)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(stream, "IHDR", header);
        if (padding > 0)
        {
            WriteChunk(stream, "tEXt", new byte[Math.Max(0, padding - 12)]);
        }

        WriteChunk(stream, "IEND", []);
        return stream.ToArray();

        static void WriteChunk(MemoryStream stream, string type, byte[] data)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
            stream.Write(length);
            stream.Write(Encoding.ASCII.GetBytes(type));
            stream.Write(data);
            stream.Write(type == "IEND" ? [0xAE, 0x42, 0x60, 0x82] : [0, 0, 0, 0]);
        }
    }

    /// <summary>最小 JPEG：SOI、APP0、SOF、SOS、EOI（影像資料不解碼）。</summary>
    internal static byte[] Jpeg(ushort width, ushort height, byte sof = 0xC0, bool fill = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange(new byte[9]);
        if (fill) bytes.Add(0xFF);
        bytes.AddRange([0xFF, sof, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03]);
        bytes.AddRange(new byte[9]);
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x02, 0x12, 0x34, 0xFF, 0xD9]);
        return [.. bytes];
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, string token, long companyId, string kind, byte[] bytes, string fileName, string contentType, int version)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName }, { new StringContent(version.ToString(CultureInfo.InvariantCulture)), "version" } };
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Path}/{kind}") { Content = form };
        Authorize(request, token, companyId);
        return client.SendAsync(request, Ct);
    }

    private static string NewCode() => "R" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private async Task<long> CreateCompanyAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(NewCode(), name, Ct);
        return company.Id;
    }

    private async Task<long> CreateRoleAsync(bool fullAccess, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var role = await scope.ServiceProvider.GetRequiredService<RoleService>()
            .CreateAsync(new RoleInput("T" + Guid.NewGuid().ToString("N")[..12], "Test role", null, DataScope.Company, fullAccess, permissions), Ct);
        return role.Id;
    }

    private async Task AssignAsync(long userId, long companyId, params long[] roleIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>()
            .AssignAsync(userId, companyId, new UserCompanyAssignment(null, null, false, true, roleIds), Ct);
    }

    private async Task<(long Id, string AccessToken)> CreateSignedInUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var username = "img" + Guid.NewGuid().ToString("N")[..20];
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user.Id, (await ReadAsync(login)).GetProperty("accessToken").GetString()!);
    }

    private static void Authorize(HttpRequestMessage request, string? token, long companyId)
    {
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, long companyId)
    {
        var request = new HttpRequestMessage(method, path);
        Authorize(request, token, companyId);
        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
