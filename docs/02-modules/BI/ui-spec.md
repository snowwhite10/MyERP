# BI interface design

WU-BI-000d reserves the existing BlueWhale interface order: 4.4.1.7.17 Cash & Bank Report, 7.18 Accounts Payable Detail, 7.19 Accounts Receivable Detail, 7.20 Client Statement, 7.21 Supplier Statement, 7.22 Other Income & Expense Detail. No page or `REQ_IO_A7.*` field ID is allocated by this design unit.

Each future page shows company, base currency, source-specific identity, inclusive dates, committed cutoff, empty/incomplete state, deterministic rows and total arithmetic. Client/Supplier Statement shows opening and closing around signed lines; Cash & Bank distinguishes book from external bank statement; Other Income & Expense exposes an export action and its frozen cutoff. A disabled action has a visible reason. Three languages and light/dark screenshots are required at page implementation.

SRS 4.4 and 4.5 and SDS 4.2 will use the same interface order. The actual field specification and prototype are deferred to the matching implementation units, not presented as completed screens.

WU-BI-003d reserves a future Dashboard Interface after the existing BlueWhale report interface order. Each card will show metric title, unit, interval or as-of date, owner cutoff, current and prior values, comparison explanation, source status and retry/disabled reason. Different owner cutoffs remain visible; an unavailable card never displays the current static home-page demonstration number as live data. Three languages, light/dark themes and actual `REQ_IO_A7` field IDs belong to the page implementation unit.
