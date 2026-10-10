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
  permission?: string;
  children?: readonly MenuItem[];
  /** data-testid（Playwright 定位） */
  testId: string;
}

export const MAIN_MENU: readonly MenuItem[] = [
  { labelKey: 'layout.menu.home', icon: 'home', route: '/', testId: 'layout-menu-home' },
  {
    labelKey: 'layout.menu.system',
    icon: 'setting',
    testId: 'layout-menu-sys',
    children: [
      { labelKey: 'layout.menu.companies', route: '/sys/companies', permission: 'SYS.COMPANY.VIEW', testId: 'layout-menu-sys-companies' },
      { labelKey: 'layout.menu.branches', route: '/sys/branches', permission: 'SYS.BRANCH.VIEW', testId: 'layout-menu-sys-branches' },
      { labelKey: 'layout.menu.departments', route: '/sys/departments', permission: 'SYS.DEPARTMENT.VIEW', testId: 'layout-menu-sys-departments' },
      { labelKey: 'layout.menu.users', route: '/sys/users', permission: 'SYS.USER.VIEW', testId: 'layout-menu-sys-users' },
      { labelKey: 'layout.menu.roles', route: '/sys/roles', permission: 'SYS.ROLE.VIEW', testId: 'layout-menu-sys-roles' },
      { labelKey: 'layout.menu.auditLogs', route: '/sys/audit-logs', permission: 'SYS.AUDIT.VIEW', testId: 'layout-menu-sys-audit-logs' },
    ],
  },
];

/** 依權限過濾選單；沒有可見子項目的群組一併隱藏。 */
export function filterMenu(
  items: readonly MenuItem[],
  hasPermission: (code: string) => boolean,
): MenuItem[] {
  return items.flatMap((item) => {
    if (item.permission && !hasPermission(item.permission)) {
      return [];
    }
    if (item.children) {
      const children = filterMenu(item.children, hasPermission);
      return children.length > 0 || item.route ? [{ ...item, children }] : [];
    }
    return [item];
  });
}
