using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-014（其他收支 manifest 半部 U 層）：以 ADR-094 manifest 核對重讀結果後才匯出其他收支 CSV。缺 manifest／
/// 重讀／表頭或表頭欄數不符為 Rejected（400）；範圍、截止點、成員、摘要或合計不符整體 Inconsistent（500），不得部分
/// 檔案；成功時忽略綁定 page size，內容與 <c>OtherIncomeExpenseRules.Export</c> 位元組相同（ADR-095、WU-BI-001a5）。
/// 不涉及 SQL、權限、API 或畫面。
/// </summary>
public sealed class OtherIncomeExpenseManifestExportRulesTests
{
    private const long Company = 7, Cutoff = 900;
    private static readonly string Digest = new('a', 64);
    private static readonly DateOnly From = new(2026, 10, 1), Through = new(2026, 10, 31);
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 4, 10, 0, TimeSpan.FromHours(8));

    private static readonly string[] Headers =
        ["業務日", "過帳時間", "類型", "來源", "單號", "類別", "類別名稱", "科目", "科目名稱", "傳票", "幣別", "總額", "報表金額"];

    private static OtherIncomeExpenseSourceRecord S(long id, decimal gross,
        OtherIncomeExpenseKind kind = OtherIncomeExpenseKind.OtherCollection, int minute = 0, string label = "利息收入",
        string reference = "OC-1") =>
        new(id, Company, kind, reference, 40 + id, label, "7101", "利息收入科目", 8000 + id,
            new DateOnly(2026, 10, 1), T0.AddMinutes(minute), OtherIncomeExpenseSourceState.Posted, 100, "TWD", gross);

    private static OtherIncomeExpenseReport Report(IReadOnlyList<OtherIncomeExpenseSourceRecord> sources,
        OtherIncomeExpenseKind? kind = null)
    {
        var r = OtherIncomeExpenseRules.Read(new OtherIncomeExpenseScope(Company, From, Through, Cutoff, "TWD", 2, kind),
            sources);
        Assert.True(r.Status == OtherIncomeExpenseStatus.Complete,
            string.Join("; ", r.Errors) + string.Join("; ", r.Incomplete));
        return r.Report!;
    }

    private static OtherIncomeExpenseReport Three() => Report([
        S(1, 1000m), S(2, 250.5m, OtherIncomeExpenseKind.OtherPayment, 1, "=HYPERLINK(1)", "OP,\"2\""),
        S(3, 10m, minute: 2),
    ]);

    private static FiReportCursorBinding Binding(int pageSize = 2, OtherIncomeExpenseKind? kind = null) =>
        new(Company, Digest, FiReportKind.OtherIncomeExpense, null, null, From, Through, kind, "TWD", pageSize);

    private static FiReportManifest Manifest(OtherIncomeExpenseReport? report = null,
        FiReportCursorBinding? binding = null)
    {
        var r = FiReportManifestRules.CreateOtherIncomeExpense(binding ?? Binding(), report ?? Three(), Created,
            TimeSpan.FromMinutes(15));
        Assert.True(r.Status == FiReportManifestStatus.Ok, string.Join("; ", r.Errors));
        return r.Manifest!;
    }

    private static void IsInconsistent(FiReportManifestExportResult e)
    {
        Assert.Equal(FiReportManifestStatus.Inconsistent, e.Status);
        Assert.Null(e.Content);
        Assert.Equal(0, e.RowCount);
    }

    // ---- Normal ----

    [Fact]
    public void Matching_reread_exports_bytes_identical_to_direct_export()
    {
        var report = Three();
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(report), Three(), Headers);

        Assert.Equal(FiReportManifestStatus.Ok, e.Status);
        Assert.Empty(e.Errors);
        Assert.Equal(3, e.RowCount);
        Assert.Equal(OtherIncomeExpenseRules.Export(report, Headers).Content, e.Content);
        Assert.Contains("'=HYPERLINK(1)", e.Content);
    }

    [Fact]
    public void Export_ignores_bound_page_size_and_renders_every_member()
    {
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(binding: Binding(pageSize: 1)), Three(),
            Headers);
        Assert.Equal(FiReportManifestStatus.Ok, e.Status);
        Assert.Equal(3, e.RowCount);
    }

    [Fact]
    public void Kind_filtered_manifest_exports_only_its_members()
    {
        var sources = new[] { S(1, 1000m), S(3, 10m, minute: 2) };
        var report = Report(sources, OtherIncomeExpenseKind.OtherCollection);
        var m = Manifest(report, Binding(kind: OtherIncomeExpenseKind.OtherCollection));
        var e = FiReportManifestRules.ExportOtherIncomeExpense(m,
            Report(sources, OtherIncomeExpenseKind.OtherCollection), Headers);
        Assert.Equal(FiReportManifestStatus.Ok, e.Status);
        Assert.Equal(2, e.RowCount);
    }

    // ---- Boundary ----

    [Fact]
    public void Empty_manifest_and_empty_reread_export_header_and_zero_totals()
    {
        var empty = Report([]);
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(empty), Report([]), Headers);
        Assert.Equal(FiReportManifestStatus.Ok, e.Status);
        Assert.Equal(0, e.RowCount);
        Assert.Equal(OtherIncomeExpenseRules.Export(empty, Headers).Content, e.Content);
    }

    [Fact]
    public void Export_does_not_check_expiry_which_stays_with_token_validation()
    {
        // No clock input: expiry is decided by FiReportCursorTokenRules.Validate with the caller's IClock (ADR-093).
        var m = Manifest() with { ExpiresAt = Created.AddMinutes(1) };
        Assert.Equal(FiReportManifestStatus.Ok,
            FiReportManifestRules.ExportOtherIncomeExpense(m, Three(), Headers).Status);
    }

    // ---- Error: request input (400) ----

    [Fact]
    public void Missing_manifest_reread_or_headers_is_rejected_in_order()
    {
        var e = FiReportManifestRules.ExportOtherIncomeExpense(null, null, null);
        Assert.Equal(FiReportManifestStatus.Rejected, e.Status);
        Assert.Null(e.Content);
        Assert.Equal(
            [
                new FiReportManifestFieldError("request", "manifest", FiReportManifestError.Required),
                new FiReportManifestFieldError("request", "reread", FiReportManifestError.Required),
                new FiReportManifestFieldError("request", "headers", FiReportManifestError.Required),
            ],
            e.Errors);
    }

    [Fact]
    public void Wrong_header_count_or_null_header_is_rejected_before_any_consistency_check()
    {
        var m = Manifest();
        var e = FiReportManifestRules.ExportOtherIncomeExpense(m, Three() with { CutoffSequence = 1 }, Headers[..^1]);
        Assert.Equal(FiReportManifestStatus.Rejected, e.Status);
        Assert.Equal([new FiReportManifestFieldError("request", "headers", FiReportManifestError.OutOfRange)],
            e.Errors);

        var withNull = Headers.Cast<string?>().ToArray();
        withNull[5] = null;
        Assert.Equal([new FiReportManifestFieldError("request", "headers", FiReportManifestError.Required)],
            FiReportManifestRules.ExportOtherIncomeExpense(m, Three(), withNull).Errors);

        Assert.Equal([new FiReportManifestFieldError("request", "headers", FiReportManifestError.OutOfRange)],
            FiReportManifestRules.ExportOtherIncomeExpense(m, Three(), [..Headers, "多一欄"]).Errors);
    }

    // ---- Error: inconsistency (500, no partial file) ----

    [Fact]
    public void Backdated_source_in_reread_is_inconsistent()
    {
        var reread = Report([
            S(1, 1000m), S(2, 250.5m, OtherIncomeExpenseKind.OtherPayment, 1, "=HYPERLINK(1)", "OP,\"2\""),
            S(3, 10m, minute: 2), S(4, 5m, minute: 3),
        ]);
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(), reread, Headers);
        IsInconsistent(e);
        Assert.Contains(new FiReportManifestFieldError("reread", "memberIds", FiReportManifestError.Inconsistent),
            e.Errors);
    }

    [Fact]
    public void Same_members_with_changed_category_label_is_digest_inconsistent_only()
    {
        var r = Three();
        var reread = r with { Rows = [r.Rows[0] with { CategoryLabel = "租金收入" }, ..r.Rows.Skip(1)] };
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(), reread, Headers);
        IsInconsistent(e);
        Assert.Equal([new FiReportManifestFieldError("reread", "contentDigest", FiReportManifestError.Inconsistent)],
            e.Errors);
    }

    [Fact]
    public void Reread_with_another_range_kind_or_cutoff_is_inconsistent()
    {
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(),
            Three() with { CutoffSequence = Cutoff + 1, Through = Through.AddDays(-1) }, Headers);
        IsInconsistent(e);
        Assert.Contains(new FiReportManifestFieldError("reread", "cutoffSequence", FiReportManifestError.Inconsistent),
            e.Errors);
        Assert.Contains(new FiReportManifestFieldError("reread", "through", FiReportManifestError.Inconsistent),
            e.Errors);

        var k = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(),
            Three() with { KindFilter = OtherIncomeExpenseKind.OtherPayment }, Headers);
        IsInconsistent(k);
        Assert.Contains(new FiReportManifestFieldError("reread", "kindFilter", FiReportManifestError.Inconsistent),
            k.Errors);
    }

    [Fact]
    public void Reread_with_tampered_totals_is_inconsistent()
    {
        var r = Three();
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(), r with { Totals = r.Totals with { Net = 1m } },
            Headers);
        IsInconsistent(e);
        Assert.Contains(e.Errors, x => x.Collection == "reread" && x.Field == "totals");
    }

    [Fact]
    public void Reread_row_with_wrong_sign_is_inconsistent_without_partial_file()
    {
        var r = Three();
        var reread = r with { Rows = [r.Rows[0], r.Rows[1] with { SignedBase = 250.5m }, r.Rows[2]] };
        var e = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(), reread, Headers);
        IsInconsistent(e);
        Assert.All(e.Errors, x => Assert.Equal(FiReportManifestError.Inconsistent, x.Error));
        Assert.Contains(e.Errors, x => x.Field == "contentDigest");
    }

    [Fact]
    public void Corrupt_or_ar_ap_manifest_is_inconsistent()
    {
        var m = Manifest();
        IsInconsistent(FiReportManifestRules.ExportOtherIncomeExpense(m with { ContentDigest = "x" }, Three(), Headers));

        var ar = m with
        {
            Binding = new(Company, Digest, FiReportKind.ArDetail, null, new DateOnly(2026, 10, 31), null, null, null,
                "TWD", 2),
            OtherTotals = null,
            ArApTotals = new(0, 0, 0, 0, 3, 0),
        };
        var e = FiReportManifestRules.ExportOtherIncomeExpense(ar, Three(), Headers);
        IsInconsistent(e);
        Assert.Contains(new FiReportManifestFieldError("manifest", "kind", FiReportManifestError.Inconsistent),
            e.Errors);
    }

    [Fact]
    public void Page_and_export_report_the_same_inconsistencies_for_the_same_reread()
    {
        var r = Three();
        var reread = r with { Rows = [r.Rows[1], r.Rows[0], r.Rows[2]] };
        var page = FiReportManifestRules.PageOtherIncomeExpense(Manifest(), reread, 1);
        var export = FiReportManifestRules.ExportOtherIncomeExpense(Manifest(), reread, Headers);
        IsInconsistent(export);
        Assert.Equal(page.Errors, export.Errors);
    }
}
