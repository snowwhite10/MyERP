The Settings module contains the master data and system administration functions that every business module of MyERP depends on. Master data (code MD) and system administration (code SYS) share the module number 8 so that requirement IDs follow the BlueWhale baseline, but they are separate projects in the modular monolith and other modules may only reach them through published contracts.

Master data is isolated by company. A user works in one selected company at a time, and every list, lookup and validation only sees the master records of that company. Codes are unique within a company, stored in upper case and never reused, even after a record is deleted. Records that are already referenced by other records or documents cannot be deleted; they can only be deactivated, so that historical documents keep a valid reference.

The master data functions are:

- Client Management and Supplier Management: maintain business partners with code, name, category, default payment term and default settlement method; maintain contacts and delivery addresses; import partners from an Excel or CSV template with a preview of row errors before commit. New partners start as Pending and must be submitted to the approval workflow; only approved partners can be used in transactions, and a partner under approval cannot be edited or deleted.
- Client Category and Supplier Category: flat category lists used to group partners.
- Product Management: maintain items with base unit of measurement, brand, category, specification, item-specific unit conversions, product images (one primary image) and free-form attributes; import items from a template. The base unit is locked once the item has item-specific conversions or is used by stock, transactions or documents.
- Item Category and Item Brand: a category tree of up to five levels and a flat brand list.
- Unit of Measurement: units with decimal places and company-level conversion factors; quantities are converted along the conversion graph and rounded by the target unit precision.
- Warehouse Management: warehouses and their storage locations.
- Currency and Exchange Rate: currencies with their decimal places and dated exchange rates; amounts are rounded by the currency precision.
- Tax Code and Tax Rate: tax codes with rates that are valid for non-overlapping date periods; a shared calculator returns net, tax and gross amounts for tax-inclusive or tax-exclusive prices.
- Settlement Method and Payment Term: settlement methods (for example cash or bank transfer) and payment terms that calculate the due date by net days, by closing day plus days, or by a fixed day in a following month.
- Chart of Accounts: a hierarchical account tree with account type and normal balance; only active leaf accounts can be posted.
- Income & Expense Category, Process Category and Process: categories for other income and expense documents, and production processes with optional standard piece rate.

The system administration functions are company, branch and department maintenance; users, password policy and account lockout; login with JWT, refresh token rotation and captcha; roles, permissions and data scope; IP whitelist; audit log; document number rules; the approval workflow engine; system parameters, print settings, logo and stamp; in-app notifications; and the shared dialogs and page templates used by all modules.

All Settings pages support the three user interface languages (Traditional Chinese, Simplified Chinese and English) and the light and dark themes. Every create, edit, delete, approve and reject operation is protected by a permission code and is written to the audit log with the field changes.
