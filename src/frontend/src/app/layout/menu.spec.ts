import { filterMenu, MAIN_MENU, MenuItem } from './menu';

const MENU: MenuItem[] = [
  { labelKey: 'layout.menu.home', route: '/', testId: 'home' },
  {
    labelKey: 'sal.menu.root',
    testId: 'sal',
    children: [
      { labelKey: 'sal.menu.order', route: '/sal/orders', permission: 'SAL.ORDER.VIEW', testId: 'sal-order' },
      { labelKey: 'sal.menu.quote', route: '/sal/quotes', permission: 'SAL.QUOTE.VIEW', testId: 'sal-quote' },
    ],
  },
  { labelKey: 'sys.menu.audit', route: '/sys/audit', permission: 'SYS.AUDIT.VIEW', testId: 'sys-audit' },
];

describe('menu', () => {
  it('main menu items have i18n keys in the layout scope and test ids', () => {
    for (const item of MAIN_MENU) {
      expect(item.labelKey).toMatch(/^layout\.menu\./);
      expect(item.testId).toMatch(/^layout-menu-/);
    }
  });

  it('keeps everything when every permission is granted', () => {
    expect(filterMenu(MENU, () => true)).toEqual(MENU);
  });

  it('hides items without VIEW permission', () => {
    const result = filterMenu(MENU, (code) => code === 'SAL.ORDER.VIEW');

    expect(result.map((i) => i.testId)).toEqual(['home', 'sal']);
    expect(result[1].children?.map((i) => i.testId)).toEqual(['sal-order']);
  });

  it('hides groups whose children are all hidden', () => {
    const result = filterMenu(MENU, () => false);

    expect(result.map((i) => i.testId)).toEqual(['home']);
  });

  it('keeps a group with its own route even when children are hidden', () => {
    const menu: MenuItem[] = [
      {
        labelKey: 'x',
        route: '/x',
        testId: 'x',
        children: [{ labelKey: 'y', route: '/x/y', permission: 'X.Y.VIEW', testId: 'y' }],
      },
    ];

    expect(filterMenu(menu, () => false)).toEqual([{ ...menu[0], children: [] }]);
  });

  it('shows an item that accepts any of several permissions', () => {
    const menu: MenuItem[] = [{ labelKey: 'x', route: '/x', permission: ['X.A', 'X.B'], testId: 'x' }];

    expect(filterMenu(menu, (code) => code === 'X.B').map((i) => i.testId)).toEqual(['x']);
    expect(filterMenu(menu, (code) => code === 'X.C')).toEqual([]);
  });

  it('hides a group that requires a missing permission', () => {
    const menu: MenuItem[] = [
      { labelKey: 'x', permission: 'X.VIEW', testId: 'x', children: [{ labelKey: 'y', route: '/y', testId: 'y' }] },
    ];

    expect(filterMenu(menu, () => false)).toEqual([]);
  });
});
