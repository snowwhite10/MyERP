using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>
/// SD-INVOICE-U09～U10 (WU-SD-007a3a, ADR-178): how the ISSUE command maps a non-Posted FI result and when it accepts
/// an FI receipt as proof of the saved invoice (BR-SD-041, BR-FI-010).
/// </summary>
public sealed class SalesInvoiceIssueServiceRulesTests
{
    private static readonly SalesInvoicePostingCommand Command = new(5, 9, 21, 1, "abc");

    private static readonly SalesInvoice Invoice = new()
    {
        Id = 9, CompanyId = 5, DocumentNo = "SI1", RequestKey = "K", RequestFingerprint = "abc", CustomerId = 21,
        CurrencyCode = "USD", BaseCurrencyCode = "TWD", ExchangeRate = 31m, OriginalGross = 35m, BaseGross = 1085m
    };

    private static readonly SalesInvoicePostingReceipt Receipt =
        new(5, 9, 21, 1, "abc", "USD", 35m, "TWD", 1085m, 101, 102, 103);

    [Fact]
    public void ReceiptMustProveIdentityCurrencyAndBothGrossAmounts()
    {
        // SD-INVOICE-U09
        SalesInvoiceIssueService.Matches(Receipt, Command, Invoice).Should().BeTrue();
        foreach (var wrong in new[]
                 {
                     Receipt with { CompanyId = 6 }, Receipt with { InvoiceId = 8 }, Receipt with { CustomerId = 22 },
                     Receipt with { SourceVersion = 2 }, Receipt with { Fingerprint = "ABC" },
                     Receipt with { Currency = "TWD" }, Receipt with { GrossAmount = 35.0001m },
                     Receipt with { BaseCurrency = "USD" }, Receipt with { BaseGrossAmount = 1084m },
                     Receipt with { SalesSourceId = 0 }, Receipt with { ReceivableId = 0 },
                     Receipt with { JournalId = -1 }
                 })
            SalesInvoiceIssueService.Matches(wrong, Command, Invoice).Should().BeFalse(wrong.ToString());
    }

    [Fact]
    public void RejectedResultsKeepFiCodesAndEverythingElseIsAnInternalFailure()
    {
        // SD-INVOICE-U10
        Map(SalesPostingOutcomeKind.Rejected, 404, SalesPostingErrors.SourceNotFound)
            .Should().BeOfType<NotFoundException>().Which.ErrorCode.Should().Be(SalesPostingErrors.SourceNotFound);
        Map(SalesPostingOutcomeKind.Rejected, 409, "FI.CREDIT.LOCK_TIMEOUT")
            .Should().BeOfType<ConflictException>().Which.ErrorCode.Should().Be("FI.CREDIT.LOCK_TIMEOUT");
        var account = Map(SalesPostingOutcomeKind.Rejected, 422, SalesPostingErrors.AccountInvalid, line: 2)
            .Should().BeOfType<BusinessRuleException>().Subject;
        account.ErrorCode.Should().Be(SalesPostingErrors.AccountInvalid);
        account.Extensions.Should().ContainKey("journalLineNo").WhoseValue.Should().Be(2);
        Map(SalesPostingOutcomeKind.Rejected, 422, SalesPostingErrors.MappingMissing)
            .Should().BeOfType<BusinessRuleException>().Which.Extensions.Should().BeNull();

        // SD built a bad command (400), FI misconfigured (500), a Fault, or Unchanged for a new invoice.
        Map(SalesPostingOutcomeKind.Rejected, 400, SalesPostingErrors.InvalidInput)
            .Should().BeOfType<SalesInvoicePostingException>().Which.ErrorCode.Should().Be(SalesPostingErrors.InvalidInput);
        Map(SalesPostingOutcomeKind.Rejected, 500, SalesPostingErrors.SourceContractUnavailable)
            .Should().BeOfType<SalesInvoicePostingException>();
        Map(SalesPostingOutcomeKind.Fault, 500, SalesPostingErrors.WriteFailed)
            .Should().BeOfType<SalesInvoicePostingException>().Which.ErrorCode.Should().Be(SalesPostingErrors.WriteFailed);
        Map(SalesPostingOutcomeKind.Unchanged, 200, null)
            .Should().BeOfType<SalesInvoicePostingException>().Which.ErrorCode
            .Should().Be(SalesInvoiceIssueService.PostingFailed);
        Map(SalesPostingOutcomeKind.Rejected, 422, null).Should().BeOfType<SalesInvoicePostingException>();
    }

    private static Exception Map(SalesPostingOutcomeKind kind, int status, string? code, int? line = null) =>
        SalesInvoiceIssueService.Failure(new SalesInvoicePostingResult(kind, false, status, code, InvalidLine: line));
}
