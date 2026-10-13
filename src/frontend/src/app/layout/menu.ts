/**
 * 側邊選單定義。各模組完成時在此加入項目（route 對應 /{prefix}/{resource}）。
 * permission 為 VIEW 權限代碼（模組.資源.VIEW）；WU-SYS-004 接上權限服務後，無權限的項目不顯示。
 */
export interface MenuItem {
  /** i18n key */
  labelKey: string;
  /** ng-zorro 圖示名稱 */
  icon?: string;
  route?: string;
  /** 需要的權限；陣列表示擁有任一即可 */
  permission?: string | readonly string[];
  children?: readonly MenuItem[];
  /** data-testid（Playwright 定位） */
  testId: string;
}

export const MAIN_MENU: readonly MenuItem[] = [
  { labelKey: 'layout.menu.home', icon: 'home', route: '/', testId: 'layout-menu-home' },
  { labelKey: 'layout.menu.inventory', icon: 'database', testId: 'layout-menu-inv', children: [
    { labelKey: 'layout.menu.stockQuery', route: '/inv/stock-query',
      permission: ['INV.INVENTORY.VIEW', 'INV.MOVEMENT.VIEW'], testId: 'layout-menu-inv-stock-query' },
    { labelKey: 'layout.menu.otherReceipts', route: '/inv/other-receipts',
      permission: 'INV.OTHER_RECEIPT.VIEW', testId: 'layout-menu-inv-other-receipts' },
    { labelKey: 'layout.menu.otherIssues', route: '/inv/other-issues',
      permission: 'INV.OTHER_ISSUE.VIEW', testId: 'layout-menu-inv-other-issues' },
  ] },
  {
    labelKey: 'layout.menu.masterData', icon: 'database', testId: 'layout-menu-md',
    children: [
      { labelKey: 'layout.menu.customers', route: '/md/customers',
        permission: 'MD.CUSTOMER.VIEW', testId: 'layout-menu-md-customers' },
      { labelKey: 'layout.menu.customerCategories', route: '/md/customer-categories',
        permission: 'MD.CUSTOMER_CATEGORY.VIEW', testId: 'layout-menu-md-customer-categories' },
      { labelKey: 'layout.menu.suppliers', route: '/md/suppliers',
        permission: 'MD.SUPPLIER.VIEW', testId: 'layout-menu-md-suppliers' },
      { labelKey: 'layout.menu.supplierCategories', route: '/md/supplier-categories',
        permission: 'MD.SUPPLIER_CATEGORY.VIEW', testId: 'layout-menu-md-supplier-categories' },
      { labelKey: 'layout.menu.uoms', route: '/md/uoms', permission: 'MD.UOM.VIEW', testId: 'layout-menu-md-uoms' },
      { labelKey: 'layout.menu.items', route: '/md/items', permission: 'MD.ITEM.VIEW', testId: 'layout-menu-md-items' },
      { labelKey: 'layout.menu.itemCategories', route: '/md/item-categories',
        permission: 'MD.ITEM_CATEGORY.VIEW', testId: 'layout-menu-md-item-categories' },
      { labelKey: 'layout.menu.itemBrands', route: '/md/item-brands',
        permission: 'MD.ITEM_BRAND.VIEW', testId: 'layout-menu-md-item-brands' },
      { labelKey: 'layout.menu.itemAttributes', route: '/md/item-attributes',
        permission: 'MD.ITEM_ATTRIBUTE.VIEW', testId: 'layout-menu-md-item-attributes' },
      { labelKey: 'layout.menu.warehouses', route: '/md/warehouses',
        permission: 'MD.WAREHOUSE.VIEW', testId: 'layout-menu-md-warehouses' },
      { labelKey: 'layout.menu.currencies', route: '/md/currencies',
        permission: 'MD.CURRENCY.VIEW', testId: 'layout-menu-md-currencies' },
      { labelKey: 'layout.menu.taxCodes', route: '/md/tax-codes',
        permission: 'MD.TAX_CODE.VIEW', testId: 'layout-menu-md-tax-codes' },
      { labelKey: 'layout.menu.accounts', route: '/md/accounts',
        permission: 'MD.ACCOUNT.VIEW', testId: 'layout-menu-md-accounts' },
      { labelKey: 'layout.menu.settlementMethods', route: '/md/settlement-methods',
        permission: 'MD.SETTLEMENT_METHOD.VIEW', testId: 'layout-menu-md-settlement-methods' },
      { labelKey: 'layout.menu.paymentTerms', route: '/md/payment-terms',
        permission: 'MD.PAYMENT_TERM.VIEW', testId: 'layout-menu-md-payment-terms' },
      { labelKey: 'layout.menu.fundAccounts', route: '/md/fund-accounts',
        permission: 'MD.FUND_ACCOUNT.VIEW', testId: 'layout-menu-md-fund-accounts' },
      { labelKey: 'layout.menu.incomeExpenseCategories', route: '/md/income-expense-categories',
        permission: 'MD.INCOME_EXPENSE_CATEGORY.VIEW', testId: 'layout-menu-md-income-expense-categories' },
      { labelKey: 'layout.menu.processCategories', route: '/md/process-categories',
        permission: 'MD.PROCESS_CATEGORY.VIEW', testId: 'layout-menu-md-process-categories' },
      { labelKey: 'layout.menu.processes', route: '/md/processes', permission: 'MD.PROCESS.VIEW', testId: 'layout-menu-md-processes' },
    ],
  },
  {
    labelKey: 'layout.menu.system',
    icon: 'setting',
    testId: 'layout-menu-sys',
    children: [
      { labelKey: 'layout.menu.notifications', route: '/sys/notifications', permission: 'SYS.NOTIFICATION.VIEW', testId: 'layout-menu-sys-notifications' },
      { labelKey: 'layout.menu.companies', route: '/sys/companies', permission: 'SYS.COMPANY.VIEW', testId: 'layout-menu-sys-companies' },
      { labelKey: 'layout.menu.branches', route: '/sys/branches', permission: 'SYS.BRANCH.VIEW', testId: 'layout-menu-sys-branches' },
      { labelKey: 'layout.menu.departments', route: '/sys/departments', permission: 'SYS.DEPARTMENT.VIEW', testId: 'layout-menu-sys-departments' },
      { labelKey: 'layout.menu.users', route: '/sys/users', permission: 'SYS.USER.VIEW', testId: 'layout-menu-sys-users' },
      { labelKey: 'layout.menu.roles', route: '/sys/roles', permission: 'SYS.ROLE.VIEW', testId: 'layout-menu-sys-roles' },
      { labelKey: 'layout.menu.numberRules', route: '/sys/number-rules', permission: 'SYS.NUMBER_RULE.VIEW', testId: 'layout-menu-sys-number-rules' },
      { labelKey: 'layout.menu.approvals', route: '/sys/approvals', permission: ['SYS.APPROVAL.PROCESS', 'SYS.APPROVAL.VIEW'], testId: 'layout-menu-sys-approvals' },
      { labelKey: 'layout.menu.approvalFlows', route: '/sys/approval-flows', permission: 'SYS.APPROVAL_FLOW.VIEW', testId: 'layout-menu-sys-approval-flows' },
      {
        labelKey: 'layout.menu.approvalDelegations',
        route: '/sys/approval-delegations',
        permission: 'SYS.APPROVAL_DELEGATION.VIEW',
        testId: 'layout-menu-sys-approval-delegations',
      },
      { labelKey: 'layout.menu.auditLogs', route: '/sys/audit-logs', permission: 'SYS.AUDIT.VIEW', testId: 'layout-menu-sys-audit-logs' },
      { labelKey: 'layout.menu.ipWhitelist', route: '/sys/ip-whitelist', permission: 'SYS.IP_WHITELIST.VIEW', testId: 'layout-menu-sys-ip-whitelist' },
      { labelKey: 'layout.menu.parameters', route: '/sys/parameters', permission: 'SYS.PARAMETER.VIEW', testId: 'layout-menu-sys-parameters' },
      { labelKey: 'layout.menu.printSettings', route: '/sys/print-settings', permission: 'SYS.PARAMETER.VIEW', testId: 'layout-menu-sys-print-settings' },
      { labelKey: 'layout.menu.logoStamp', route: '/sys/logo-stamp', permission: 'SYS.PARAMETER.VIEW', testId: 'layout-menu-sys-logo-stamp' },
    ],
  },
];

/** 依權限過濾選單；沒有可見子項目的群組一併隱藏。 */
export function filterMenu(
  items: readonly MenuItem[],
  hasPermission: (code: string) => boolean,
): MenuItem[] {
  return items.flatMap((item) => {
    if (item.permission && !(typeof item.permission === 'string' ? [item.permission] : item.permission).some(hasPermission)) {
      return [];
    }
    if (item.children) {
      const children = filterMenu(item.children, hasPermission);
      return children.length > 0 || item.route ? [{ ...item, children }] : [];
    }
    return [item];
  });
}
