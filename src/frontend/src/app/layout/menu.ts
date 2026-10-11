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
