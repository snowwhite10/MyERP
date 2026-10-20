import { RequisitionPage } from './requisition-page';

describe('RequisitionPage', () => {
  it('allows actions only in matching saved states', () => {
    expect(RequisitionPage.canSubmit('Draft')).toBe(true);
    expect(RequisitionPage.canSubmit('Rejected')).toBe(false);
    expect(RequisitionPage.canEdit('Rejected')).toBe(true);
    expect(RequisitionPage.canDelete('PendingApproval')).toBe(false);
  });

  it('rejects invalid list filters before sending them', () => {
    expect(RequisitionPage.validFilters('2026-10-21', '2026-10-20', null)).toBe(false);
    expect(RequisitionPage.validFilters('', '', 0)).toBe(false);
    expect(RequisitionPage.validFilters('2026-10-20', '2026-10-20', 7)).toBe(true);
  });
});
