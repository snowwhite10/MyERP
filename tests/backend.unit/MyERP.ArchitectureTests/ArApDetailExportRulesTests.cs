using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-014（AR／AP 純規則部分）與 FI-RPT-005／006／009 匯出 U 層：AR／AP 明細匯出。輸出公司／方向／夥伴篩選／
/// asOf／cutoff／本位幣抬頭行、呼叫端已譯表頭、全部列（含分配 ID 數）及全集合 original／credit／settled／open 合計尾行；
/// 文字防公式注入，數字與日期 invariant，CRLF、無 BOM；列與合計不一致或超過 10,000 列整體拒絕，不得部分檔案。
/// 以 ADR-094 manifest 核對重讀結果後才匯出，不符整體 Inconsistent（500）（ADR-095、WU-BI-001a4）。
/// 不涉及 SQL、權限、API、BI Excel 或畫面。
/// </summary>
public sealed class ArApDetailExportRulesTests
{
    private const long Company = 7, Cutoff = 900, Partner = 31;
    private static readonly string Digest = new('a', 64);
    private static readonly DateOnly AsOf = new(2026, 10, 31);
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 3, 50, 0, TimeSpan.FromHours(8));

    private static readonly string[] Headers =
    [
        "業務日", "過帳時間", "夥伴ID", "夥伴", "本金ID", "來源模組", "來源類型", "來源ID", "單號", "傳票ID", "本位幣",
        "單據幣", "原始", "貸項", "已結算", "未清", "分配數",
    ];

    private static ArApPrincipalRecord P(long id, decimal original, int minute = 0, long partner = Partner,
        ArApDirection direction = ArApDirection.Receivable, string label = "客戶甲", string? reference = null,
        DateOnly? date = null) =>
        new(id, Company, direction, partner, label, "SD", "Invoice", 500 + id, reference ?? "INV-" + id, 8000 + id,
            date ?? new DateOnly(2026, 10, 1), T0.AddMinutes(minute), ArApSourceState.Posted, 100, "TWD", "USD",
            original);

    private static ArApAllocationRecord A(long id, long principal, decimal amount,
        ArApAllocationKind kind = ArApAllocationKind.Settlement) =>
        new(id, Company, principal, kind, new DateOnly(2026, 10, 2), ArApSourceState.Posted, 100, "TWD", amount,
            9000 + id);

    private static ArApDetailReport Report(IReadOnlyList<ArApPrincipalRecord> principals,
        IReadOnlyList<ArApAllocationRecord>? allocations = null, long? partner = null,
        ArApDirection direction = ArApDirection.Receivable, bool includeZero = true)
    {
        var r = ArApDetailAsOfRules.Read(
            new ArApDetailScope(Company, direction, partner, AsOf, Cutoff, "TWD", 2, includeZero),
            principals, allocations ?? []);
        Assert.True(r.Status == ArApDetailStatus.Complete, string.Join("; ", r.Errors) + string.Join("; ", r.Incomplete));
        return r.Report!;
    }

    private static ArApDetailReport Three() => Report(
        [P(1, 1000m), P(2, 500.25m, 1, label: "=HYPERLINK(1)", reference: "INV,\"2\""), P(3, 80m, 2)],
        [A(10, 1, 100m, ArApAllocationKind.Credit), A(11, 1, 250.5m), A(12, 3, 80m)]);

    private static FiReportCursorBinding Binding(FiReportKind kind = FiReportKind.ArDetail, long? partner = null) =>
        new(Company, Digest, kind, partner, AsOf, null, null, null, "TWD", 2);

    private static FiReportManifest Manifest(ArApDetailReport report, FiReportCursorBinding? binding = null)
    {
        var r = FiReportManifestRules.CreateArAp(binding ?? Binding(), report, Created, TimeSpan.FromMinutes(15));
        Assert.True(r.Status == FiReportManifestStatus.Ok, string.Join("; ", r.Errors));
        return r.Manifest!;
    }

    // ---- Normal ----

    [Fact]
    public void Export_renders_scope_lines_headers_rows_in_report_order_and_full_set_totals()
    {
        var e = ArApDetailAsOfRules.Export(Three(), Headers);

        Assert.True(e.IsValid, string.Join("; ", e.Errors));
        Assert.Empty(e.Errors);
        Assert.Equal(3, e.RowCount);
        var lines = e.Content!.Split("\r\n");
        Assert.Equal("CompanyId,7", lines[0]);
        Assert.Equal("Direction,Receivable", lines[1]);
        Assert.Equal("PartnerId,", lines[2]);
        Assert.Equal("AsOf,2026-10-31", lines[3]);
        Assert.Equal("CutoffSequence,900", lines[4]);
        Assert.Equal("BaseCurrency,TWD", lines[5]);
        Assert.Equal(string.Join(',', Headers), lines[6]);
        Assert.Equal("2026-10-01,2026-10-01T09:00:00.0000000+08:00,31,客戶甲,1,SD,Invoice,501,INV-1,8001,TWD,USD,1000,100,250.5,649.5,2",
            lines[7]);
        Assert.Equal("2026-10-01,2026-10-01T09:01:00.0000000+08:00,31,'=HYPERLINK(1),2,SD,Invoice,502,\"INV,\"\"2\"\"\",8002,TWD,USD,500.25,0,0,500.25,0",
            lines[8]);
        Assert.Equal("2026-10-01,2026-10-01T09:02:00.0000000+08:00,31,客戶甲,3,SD,Invoice,503,INV-3,8003,TWD,USD,80,0,80,0,1",
            lines[9]);
        Assert.Equal(["Original,1580.25", "Credit,100", "Settled,330.5", "Open,1149.75"], lines[10..]);
        Assert.Equal(14, lines.Length);
        Assert.False(e.Content.StartsWith('﻿'));
        Assert.DoesNotContain("\n", e.Content.Replace("\r\n", ""));
    }

    [Fact]
    public void Ap_partner_filtered_export_states_direction_and_partner()
    {
        var report = Report([P(1, 10m, direction: ArApDirection.Payable)], partner: Partner,
            direction: ArApDirection.Payable);
        var lines = ArApDetailAsOfRules.Export(report, Headers).Content!.Split("\r\n");
        Assert.Equal("Direction,Payable", lines[1]);
        Assert.Equal("PartnerId,31", lines[2]);
    }

    [Fact]
    public void Export_is_deterministic_for_the_same_report()
    {
        Assert.Equal(ArApDetailAsOfRules.Export(Three(), Headers).Content,
            ArApDetailAsOfRules.Export(Three(), Headers).Content);
    }

    [Fact]
    public void Header_text_is_escaped_too()
    {
        var headers = Headers.ToArray();
        headers[0] = "+業務日";
        var lines = ArApDetailAsOfRules.Export(Three(), headers).Content!.Split("\r\n");
        Assert.StartsWith("'+業務日,", lines[6]);
    }

    // ---- Boundary ----

    [Fact]
    public void Empty_report_has_scope_header_and_zero_totals()
    {
        var e = ArApDetailAsOfRules.Export(Report([]), Headers);
        Assert.True(e.IsValid);
        Assert.Equal(0, e.RowCount);
        var lines = e.Content!.Split("\r\n");
        Assert.Equal(11, lines.Length);
        Assert.Equal(["Original,0", "Credit,0", "Settled,0", "Open,0"], lines[^4..]);
    }

    [Fact]
    public void Ten_thousand_rows_export_and_one_more_is_rejected_whole()
    {
        var max = Fake(ArApDetailAsOfRules.MaxExportRows);
        var ok = ArApDetailAsOfRules.Export(max, Headers);
        Assert.True(ok.IsValid, string.Join("; ", ok.Errors));
        Assert.Equal(ArApDetailAsOfRules.MaxExportRows, ok.RowCount);

        var e = ArApDetailAsOfRules.Export(Fake(ArApDetailAsOfRules.MaxExportRows + 1), Headers);
        Assert.False(e.IsValid);
        Assert.Null(e.Content);
        Assert.Equal(0, e.RowCount);
        Assert.Contains(new ArApDetailFieldError("report", null, "rows", ArApDetailError.OutOfRange), e.Errors);
    }

    /// <summary>Builds a consistent report of <paramref name="n"/> rows without running the read.</summary>
    private static ArApDetailReport Fake(int n)
    {
        var template = Three().Rows[0];
        var rows = Enumerable.Range(1, n)
            .Select(i => template with
            {
                PrincipalId = i, SourceId = 500 + i, OriginalBase = 2m, CreditBase = 0m, SettlementBase = 1m,
                OpenBase = 1m, AllocationIds = [i], AllocationCount = 1, IsOutstanding = true,
            })
            .ToList();
        return Three() with { Rows = rows, Totals = new(2m * n, 0m, n, n, n, n) };
    }

    [Fact]
    public void Amount_scale_from_storage_is_rendered_as_is_and_negative_open_never_appears()
    {
        var report = Report([P(1, 12.30m)], [A(1, 1, 0.30m)]);
        var line = ArApDetailAsOfRules.Export(report, Headers).Content!.Split("\r\n")[7];
        Assert.EndsWith(",12.30,0,0.30,12.00,1", line);
    }

    // ---- Error: input and inconsistency (no partial file) ----

    [Fact]
    public void Missing_report_and_headers_are_both_reported()
    {
        var e = ArApDetailAsOfRules.Export(null, null);
        Assert.False(e.IsValid);
        Assert.Null(e.Content);
        Assert.Equal(
            [
                new ArApDetailFieldError("report", null, "report", ArApDetailError.Required),
                new ArApDetailFieldError("request", null, "headers", ArApDetailError.Required),
            ],
            e.Errors);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(0)]
    public void Wrong_header_count_is_rejected(int count)
    {
        var headers = Enumerable.Range(0, count).Select(i => "h" + i).ToArray();
        var e = ArApDetailAsOfRules.Export(Three(), headers);
        Assert.Null(e.Content);
        Assert.Contains(new ArApDetailFieldError("request", null, "headers", ArApDetailError.OutOfRange), e.Errors);
    }

    [Fact]
    public void Null_header_cell_is_rejected_with_its_index()
    {
        var headers = Headers.Cast<string?>().ToArray();
        headers[4] = null;
        var e = ArApDetailAsOfRules.Export(Three(), headers);
        Assert.Null(e.Content);
        Assert.Equal([new ArApDetailFieldError("headers", 4, "header", ArApDetailError.Required)], e.Errors);
    }

    public static TheoryData<string, Func<ArApDetailReport, ArApDetailReport>> Tampered => new()
    {
        { "rows", r => r with { Rows = [r.Rows[0] with { OpenBase = 1m }, ..r.Rows.Skip(1)] } },
        { "rows", r => r with { Rows = [r.Rows[0] with { IsOutstanding = false }, ..r.Rows.Skip(1)] } },
        { "rows", r => r with { Rows = [r.Rows[0] with { AllocationCount = 3 }, ..r.Rows.Skip(1)] } },
        { "rows", r => r with { Rows = [r.Rows[0] with { Direction = ArApDirection.Payable }, ..r.Rows.Skip(1)] } },
        { "rows", r => r with { Rows = [r.Rows[0] with { CompanyId = 8 }, ..r.Rows.Skip(1)] } },
        { "rows", r => r with { Rows = [r.Rows[0] with { BaseCurrency = "USD" }, ..r.Rows.Skip(1)] } },
        { "rows", r => r with { Rows = [r.Rows[0], r.Rows[0], r.Rows[2]] } },
        { "rows", r => r with { Rows = [r.Rows[0] with { OriginalBase = decimal.MaxValue, CreditBase = -1m }, ..r.Rows.Skip(1)] } },
        { "totals", r => r with { Totals = r.Totals with { Open = 0m } } },
        { "totals", r => r with { Totals = r.Totals with { RowCount = 2 } } },
        { "totals", r => r with { Totals = r.Totals with { OutstandingCount = 3 } } },
        { "totals", r => r with { Rows = r.Rows.Take(2).ToList() } },
    };

    [Theory]
    [MemberData(nameof(Tampered))]
    public void Report_whose_rows_or_totals_disagree_is_rejected_whole(string field,
        Func<ArApDetailReport, ArApDetailReport> tamper)
    {
        var e = ArApDetailAsOfRules.Export(tamper(Three()), Headers);
        Assert.False(e.IsValid);
        Assert.Null(e.Content);
        Assert.Equal([new ArApDetailFieldError("report", null, field, ArApDetailError.Inconsistent)], e.Errors);
    }

    [Fact]
    public void Consistent_rows_whose_sum_overflows_are_totals_inconsistent_not_an_exception()
    {
        var r = Three();
        var big = r.Rows[0] with
        {
            OriginalBase = decimal.MaxValue, CreditBase = 0m, SettlementBase = 0m, OpenBase = decimal.MaxValue,
            AllocationIds = [], AllocationCount = 0,
        };
        var report = r with { Rows = [big, big with { PrincipalId = 2 }] };
        var e = ArApDetailAsOfRules.Export(report, Headers);
        Assert.Null(e.Content);
        Assert.Equal([new ArApDetailFieldError("report", null, "totals", ArApDetailError.Inconsistent)], e.Errors);
    }

    // ---- Manifest-verified export (ADR-094 → ADR-095) ----

    [Fact]
    public void Manifest_export_of_matching_reread_equals_direct_export()
    {
        var report = Three();
        var m = Manifest(report);
        var e = FiReportManifestRules.ExportArAp(m, Three(), Headers);

        Assert.Equal(FiReportManifestStatus.Ok, e.Status);
        Assert.Empty(e.Errors);
        Assert.Equal(3, e.RowCount);
        Assert.Equal(ArApDetailAsOfRules.Export(report, Headers).Content, e.Content);
    }

    [Fact]
    public void Manifest_export_ignores_page_size_and_exports_every_member()
    {
        var m = Manifest(Three(), Binding() with { PageSize = 1 });
        var e = FiReportManifestRules.ExportArAp(m, Three(), Headers);
        Assert.Equal(3, e.RowCount);
    }

    [Fact]
    public void Backdated_source_in_reread_is_inconsistent_with_no_file()
    {
        var m = Manifest(Three());
        var reread = Report(
            [P(1, 1000m), P(2, 500.25m, 1, label: "=HYPERLINK(1)", reference: "INV,\"2\""), P(3, 80m, 2), P(4, 5m, 3)],
            [A(10, 1, 100m, ArApAllocationKind.Credit), A(11, 1, 250.5m), A(12, 3, 80m)]);
        var e = FiReportManifestRules.ExportArAp(m, reread, Headers);

        Assert.Equal(FiReportManifestStatus.Inconsistent, e.Status);
        Assert.Null(e.Content);
        Assert.Equal(0, e.RowCount);
        Assert.Contains(new FiReportManifestFieldError("reread", "memberIds", FiReportManifestError.Inconsistent),
            e.Errors);
    }

    [Fact]
    public void Same_members_with_changed_label_is_digest_inconsistent()
    {
        var m = Manifest(Three());
        var r = Three();
        var reread = r with { Rows = [r.Rows[0] with { PartnerLabel = "客戶乙" }, ..r.Rows.Skip(1)] };
        var e = FiReportManifestRules.ExportArAp(m, reread, Headers);
        Assert.Equal(FiReportManifestStatus.Inconsistent, e.Status);
        Assert.Equal([new FiReportManifestFieldError("reread", "contentDigest", FiReportManifestError.Inconsistent)],
            e.Errors);
    }

    [Fact]
    public void Reread_with_another_scope_or_cutoff_is_inconsistent()
    {
        var m = Manifest(Three());
        var e = FiReportManifestRules.ExportArAp(m, Three() with { CutoffSequence = Cutoff + 1, AsOf = AsOf.AddDays(-1) },
            Headers);
        Assert.Equal(FiReportManifestStatus.Inconsistent, e.Status);
        Assert.Contains(new FiReportManifestFieldError("reread", "cutoffSequence", FiReportManifestError.Inconsistent),
            e.Errors);
        Assert.Contains(new FiReportManifestFieldError("reread", "asOf", FiReportManifestError.Inconsistent), e.Errors);
    }

    [Fact]
    public void Reread_with_tampered_totals_is_inconsistent()
    {
        var m = Manifest(Three());
        var r = Three();
        var e = FiReportManifestRules.ExportArAp(m, r with { Totals = r.Totals with { Open = 1m } }, Headers);
        Assert.Equal(FiReportManifestStatus.Inconsistent, e.Status);
        Assert.Null(e.Content);
    }

    [Fact]
    public void Corrupt_or_other_report_manifest_is_inconsistent()
    {
        var m = Manifest(Three());
        Assert.Equal(FiReportManifestStatus.Inconsistent,
            FiReportManifestRules.ExportArAp(m with { ContentDigest = "x" }, Three(), Headers).Status);
        var other = m with { Binding = new(Company, Digest, FiReportKind.OtherIncomeExpense, null, null, AsOf, AsOf,
            null, "TWD", 2), ArApTotals = null, OtherTotals = new(0, 0, 0, 3, 0, 0) };
        var e = FiReportManifestRules.ExportArAp(other, Three(), Headers);
        Assert.Equal(FiReportManifestStatus.Inconsistent, e.Status);
        Assert.Null(e.Content);
    }

    [Fact]
    public void Missing_manifest_reread_or_headers_is_rejected()
    {
        var e = FiReportManifestRules.ExportArAp(null, null, null);
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
    public void Wrong_headers_are_rejected_before_any_consistency_check()
    {
        var m = Manifest(Three());
        var e = FiReportManifestRules.ExportArAp(m, Three() with { CutoffSequence = 1 }, Headers[..^1]);
        Assert.Equal(FiReportManifestStatus.Rejected, e.Status);
        Assert.Equal([new FiReportManifestFieldError("request", "headers", FiReportManifestError.OutOfRange)],
            e.Errors);

        var withNull = Headers.Cast<string?>().ToArray();
        withNull[0] = null;
        Assert.Equal([new FiReportManifestFieldError("request", "headers", FiReportManifestError.Required)],
            FiReportManifestRules.ExportArAp(m, Three(), withNull).Errors);
    }

    [Fact]
    public void Manifest_export_does_not_check_expiry_which_stays_with_token_validation()
    {
        // Expiry is decided by FiReportCursorTokenRules.Validate with the caller's IClock now (ADR-093);
        // the export rule has no clock input and therefore cannot read system time.
        var m = Manifest(Three()) with { };
        Assert.Equal(FiReportManifestStatus.Ok, FiReportManifestRules.ExportArAp(m, Three(), Headers).Status);
    }
}
