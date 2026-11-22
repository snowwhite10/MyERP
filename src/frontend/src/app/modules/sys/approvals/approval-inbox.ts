import { DecimalPipe, DOCUMENT, formatNumber } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, effect, inject, Injector, LOCALE_ID, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslatePipe } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { NzAlertModule } from 'ng-zorro-antd/alert';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzTableModule, NzTableQueryParams } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';
import { PermissionService } from '../../../core/auth/permission.service';
import { CompanyContextService } from '../../../core/company/company-context.service';
import { apiErrorTraceId } from '../../../core/http/api-error';
import { ApiErrorDisplay } from '../../../shared/api-error-display';
import { displayTime } from '../../../shared/date';
import { PAGE_SIZE_OPTIONS } from '../../../shared/list-page/table-query';
import {
  APPROVAL_STATUSES,
  approvalErrorKey,
  ApprovalInstanceDetail,
  ApprovalInstanceSummary,
  ApprovalService,
  ApprovalStatus,
  approvalStatusKey,
  ApprovalTask,
  approverTypeKey,
  COMMENT_MAX_LENGTH,
  isOverrideReasonError,
  OVERRIDABLE_REFUSALS,
  overridableRefusal,
  OVERRIDE_REASON_MAX_LENGTH,
  statusColor,
  stepStatusKey,
} from './approval.service';

export type ApprovalView = 'tasks' | 'history';

/** 單據模組拒絕最終核准、但可由審批人填寫理由覆核時的超額（ADR-171、ADR-173）。 */
export interface ApprovalCreditOverride {
  excess: number;
  currency: string | null;
  /** 覆核所需權限（例如 `SAL.CREDIT.OVERRIDE`）。 */
  permission: string;
}

/** 核准／駁回對話框：對象為待辦（帶讀取時的 version）。 */
export interface ApprovalDecisionDialog {
  action: 'approve' | 'reject';
  task: ApprovalTask;
}

/**
 * 審批待辦頁（介面 8.70，REQ_F4118.29.5–7、29.9）：「我的待辦」（`SYS.APPROVAL.PROCESS`）列出本人或代理可處理的步驟，
 * 可核准或駁回（駁回必填意見）；最終核准被單據模組以可覆核的超額拒絕時（422 `SAL.CREDIT.LIMIT_EXCEEDED`），
 * 顯示超額並要求覆核理由後再送出（ADR-173）；「審批歷史」（`SYS.APPROVAL.VIEW`）依單據類型、狀態與關鍵字查詢；明細顯示各步驟的處理記錄。
 */
@Component({
  selector: 'erp-approval-inbox',
  imports: [
    DecimalPipe,
    FormsModule,
    TranslatePipe,
    NzAlertModule,
    NzButtonModule,
    NzInputModule,
    NzModalModule,
    NzSelectModule,
    NzTableModule,
    NzTagModule,
    HasPermissionDirective,
    ApiErrorDisplay,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="erp-approval-inbox">
      <header class="erp-approval-inbox__header">
        <h1 data-testid="sys-approval-title">{{ 'sys.approval.title' | translate }}</h1>
        <div class="erp-approval-inbox__views" role="tablist">
          @if (canProcess()) {
            <button nz-button role="tab" data-testid="sys-approval-view-tasks" [nzType]="view() === 'tasks' ? 'primary' : 'default'"
              [attr.aria-selected]="view() === 'tasks'" (click)="switchView('tasks')">
              {{ 'sys.approval.tasks' | translate }}
            </button>
          }
          @if (canView()) {
            <button nz-button role="tab" data-testid="sys-approval-view-history" [nzType]="view() === 'history' ? 'primary' : 'default'"
              [attr.aria-selected]="view() === 'history'" (click)="switchView('history')">
              {{ 'sys.approval.history' | translate }}
            </button>
          }
        </div>
      </header>
      <div class="erp-approval-inbox__filters">
        <input nz-input data-testid="sys-approval-document-type" [attr.aria-label]="'sys.approval.documentType' | translate"
          [placeholder]="'sys.approval.documentType' | translate" [ngModel]="documentType()" (ngModelChange)="documentType.set($event)"
          (keyup.enter)="search()" />
        <input nz-input type="search" data-testid="sys-approval-search" [attr.aria-label]="'sys.approval.search' | translate"
          [placeholder]="'sys.approval.search' | translate" [ngModel]="keyword()" (ngModelChange)="keyword.set($event)" (keyup.enter)="search()" />
        @if (view() === 'history') {
          <nz-select data-testid="sys-approval-status-filter" nzAllowClear [nzPlaceHolder]="'sys.approval.status' | translate"
            [attr.aria-label]="'sys.approval.status' | translate" [ngModel]="status()" (ngModelChange)="status.set($event ?? null)">
            @for (value of statuses; track value) {
              <nz-option [nzValue]="value" [nzLabel]="approvalStatusKey(value) | translate" />
            }
          </nz-select>
        }
        <button nz-button data-testid="sys-approval-search-button" (click)="search()">{{ 'common.action.search' | translate }}</button>
      </div>
      @if (loadError()) {
        <nz-alert nzType="error" nzShowIcon data-testid="sys-approval-load-error" [nzMessage]="loadError()! | translate" />
      }
      @if (notice()) {
        <nz-alert nzType="success" nzShowIcon nzCloseable data-testid="sys-approval-notice"
          [nzMessage]="notice()! | translate: noticeParams()" (nzOnClose)="notice.set(null)" />
      }

      @if (view() === 'tasks') {
        <nz-table #tasksTable data-testid="sys-approval-task-table" nzSize="middle" [nzData]="tasks()" [nzLoading]="loading()"
          [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
          nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="'sys.approval.noTasks' | translate" (nzQueryParams)="onQueryParams($event)">
          <thead>
            <tr>
              <th>{{ 'sys.approval.document' | translate }}</th>
              <th>{{ 'sys.approval.summary' | translate }}</th>
              <th nzAlign="right">{{ 'sys.approval.amount' | translate }}</th>
              <th>{{ 'sys.approval.submittedBy' | translate }}</th>
              <th nzColumnKey="submittedAt" [nzSortFn]="true" [nzSortOrder]="sortOrder()">{{ 'sys.approval.submittedAt' | translate }}</th>
              <th>{{ 'sys.approval.step' | translate }}</th>
              <th>{{ 'sys.approval.actions' | translate }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of tasksTable.data; track row.instanceId) {
              <tr [attr.data-testid]="'sys-approval-task-' + row.documentNo">
                <td>{{ row.documentType }}<br /><strong>{{ row.documentNo }}</strong></td>
                <td>{{ row.summary }}</td>
                <td class="erp-approval-inbox__amount">{{ row.amount | number: '1.0-4' }}</td>
                <td>{{ row.submittedByUsername }}</td>
                <td>{{ displayTime(row.submittedAt) }}</td>
                <td>
                  {{ 'sys.approval.stepOf' | translate: { no: row.stepNo, count: row.stepCount } }} {{ row.stepName }}
                  @if (row.onBehalfOfUsername) {
                    <nz-tag nzColor="purple" [attr.data-testid]="'sys-approval-on-behalf-' + row.documentNo">
                      {{ 'sys.approval.onBehalfOf' | translate: { user: row.onBehalfOfUsername } }}
                    </nz-tag>
                  }
                </td>
                <td>
                  <button nz-button nzType="link" nzSize="small" [attr.data-testid]="'sys-approval-approve-' + row.documentNo"
                    (click)="openDecision('approve', row)">
                    {{ 'sys.approval.approve' | translate }}
                  </button>
                  <button nz-button nzType="link" nzDanger nzSize="small" [attr.data-testid]="'sys-approval-reject-' + row.documentNo"
                    (click)="openDecision('reject', row)">
                    {{ 'sys.approval.reject' | translate }}
                  </button>
                  <button nz-button nzType="link" nzSize="small" *erpHasPermission="'SYS.APPROVAL.VIEW'"
                    [attr.data-testid]="'sys-approval-detail-' + row.documentNo" (click)="openDetail(row, row.instanceId)">
                    {{ 'sys.approval.detail' | translate }}
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </nz-table>
      } @else {
        <nz-table #historyTable data-testid="sys-approval-history-table" nzSize="middle" [nzData]="history()" [nzLoading]="loading()"
          [nzFrontPagination]="false" [nzTotal]="total()" [nzPageIndex]="page()" [nzPageSize]="pageSize()"
          nzShowSizeChanger [nzPageSizeOptions]="pageSizeOptions" [nzNoResult]="'sys.approval.noHistory' | translate" (nzQueryParams)="onQueryParams($event)">
          <thead>
            <tr>
              <th>{{ 'sys.approval.document' | translate }}</th>
              <th>{{ 'sys.approval.summary' | translate }}</th>
              <th nzAlign="right">{{ 'sys.approval.amount' | translate }}</th>
              <th>{{ 'sys.approval.status' | translate }}</th>
              <th>{{ 'sys.approval.submittedBy' | translate }}</th>
              <th nzColumnKey="submittedAt" [nzSortFn]="true" [nzSortOrder]="sortOrder()">{{ 'sys.approval.submittedAt' | translate }}</th>
              <th>{{ 'sys.approval.completedAt' | translate }}</th>
              <th>{{ 'sys.approval.actions' | translate }}</th>
            </tr>
          </thead>
          <tbody>
            @for (row of historyTable.data; track row.id) {
              <tr [attr.data-testid]="'sys-approval-history-' + row.id">
                <td>{{ row.documentType }}<br /><strong>{{ row.documentNo }}</strong></td>
                <td>{{ row.summary }}</td>
                <td class="erp-approval-inbox__amount">{{ row.amount | number: '1.0-4' }}</td>
                <td>
                  <nz-tag [nzColor]="statusColor(row.status)">{{ approvalStatusKey(row.status) | translate }}</nz-tag>
                  @if (row.currentStepNo) {
                    <small>{{ 'sys.approval.atStep' | translate: { no: row.currentStepNo } }}</small>
                  }
                </td>
                <td>{{ row.submittedByUsername }}</td>
                <td>{{ displayTime(row.submittedAt) }}</td>
                <td>{{ displayTime(row.completedAt) }}</td>
                <td>
                  <button nz-button nzType="link" nzSize="small" [attr.data-testid]="'sys-approval-history-detail-' + row.id"
                    (click)="openDetail(row, row.id)">
                    {{ 'sys.approval.detail' | translate }}
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </nz-table>
      }
    </section>

    <nz-modal [nzVisible]="decision() !== null" [nzTitle]="decisionTitle() | translate: decisionParams()"
      [nzOkText]="decisionOkText() | translate" [nzOkDanger]="decision()?.action === 'reject'"
      [nzCancelText]="'common.action.cancel' | translate" [nzOkLoading]="deciding()" [nzOkDisabled]="overrideBlocked()" (nzOnOk)="submitDecision()" (nzOnCancel)="closeDecision()">
      <ng-container *nzModalContent>
        @if (decisionError()) {
          <erp-api-error-display data-testid="sys-approval-decision-error" [messageKey]="decisionError()!" [traceId]="decisionTraceId()" />
        }
        @if (decision(); as current) {
          <p data-testid="sys-approval-decision-summary">
            {{ current.task.documentType }} {{ current.task.documentNo }} · {{ current.task.amount | number: '1.0-4' }}
            · {{ 'sys.approval.stepOf' | translate: { no: current.task.stepNo, count: current.task.stepCount } }} {{ current.task.stepName }}
          </p>
        }
        @if (creditOverride(); as over) {
          <nz-alert nzType="warning" nzShowIcon data-testid="sys-approval-credit-excess"
            [nzMessage]="(decisionUncertain() ? 'sys.approval.credit.excessUncertain' : 'sys.approval.credit.excess') | translate: { amount: (over.excess | number: '1.0-4'), currency: over.currency ?? '' }"
            [nzDescription]="(overrideBlocked() ? 'sys.approval.credit.needOverride' : 'sys.approval.credit.overrideHint') | translate" />
          @if (!overrideBlocked()) {
            <div class="erp-approval-inbox__field">
              <label for="approval-override-reason">
                {{ 'sys.approval.credit.reason' | translate }} <span class="erp-approval-inbox__required" aria-hidden="true">*</span>
              </label>
              <textarea nz-input id="approval-override-reason" data-testid="sys-approval-override-reason" rows="2" required autocomplete="off"
                [attr.maxlength]="overrideReasonMax" [attr.aria-invalid]="overrideReasonError() ? true : null"
                [attr.aria-describedby]="overrideReasonError() ? 'approval-override-reason-error' : 'approval-override-reason-count'"
                [placeholder]="'sys.approval.credit.reasonPlaceholder' | translate: { max: overrideReasonMax }"
                [ngModel]="overrideReason()" (ngModelChange)="overrideReason.set($event ?? ''); overrideReasonError.set(null)"></textarea>
              <small id="approval-override-reason-count" class="erp-approval-inbox__count">{{ overrideReason().trim().length }} / {{ overrideReasonMax }}</small>
              @if (overrideReasonError()) {
                <p role="alert" id="approval-override-reason-error" class="erp-approval-inbox__error" data-testid="sys-approval-override-reason-error">
                  {{ overrideReasonError()! | translate: { max: overrideReasonMax } }}
                </p>
              }
            </div>
          }
        }
        <label for="approval-comment">{{ 'sys.approval.comment' | translate }}</label>
        <textarea nz-input id="approval-comment" data-testid="sys-approval-comment" rows="3" [attr.maxlength]="commentMaxLength"
          [placeholder]="(decision()?.action === 'reject' ? 'sys.approval.commentRequired' : 'sys.approval.commentOptional') | translate"
          [ngModel]="comment()" (ngModelChange)="comment.set($event ?? '')"></textarea>
        @if (commentMissing()) {
          <p role="alert" class="erp-approval-inbox__error" data-testid="sys-approval-comment-error">
            {{ 'sys.approval.commentRequired' | translate }}
          </p>
        }
      </ng-container>
    </nz-modal>

    <nz-modal [nzVisible]="detailOpen()" [nzTitle]="'sys.approval.detailTitle' | translate: detailParams()" nzWidth="900px"
      [nzFooter]="null" (nzOnCancel)="closeDetail()">
      <ng-container *nzModalContent>
        @if (detailError()) {
          <nz-alert nzType="error" nzShowIcon data-testid="sys-approval-detail-error" [nzMessage]="detailError()! | translate" />
        }
        @if (detail(); as d) {
          <dl class="erp-approval-inbox__facts" data-testid="sys-approval-detail">
            <dt>{{ 'sys.approval.status' | translate }}</dt>
            <dd><nz-tag [nzColor]="statusColor(d.status)">{{ approvalStatusKey(d.status) | translate }}</nz-tag></dd>
            <dt>{{ 'sys.approval.amount' | translate }}</dt>
            <dd>{{ d.amount | number: '1.0-4' }}</dd>
            <dt>{{ 'sys.approval.summary' | translate }}</dt>
            <dd>{{ d.summary }}</dd>
            <dt>{{ 'sys.approval.submittedBy' | translate }}</dt>
            <dd>{{ d.submittedByUsername }} · {{ displayTime(d.submittedAt) }}</dd>
          </dl>
          <nz-table #stepsTable [nzData]="d.steps" [nzShowPagination]="false" nzSize="small" [nzNoResult]="'sys.approval.autoApproved' | translate">
            <thead>
              <tr>
                <th>#</th>
                <th>{{ 'sys.approval.stepName' | translate }}</th>
                <th>{{ 'sys.approval.approver' | translate }}</th>
                <th>{{ 'sys.approval.status' | translate }}</th>
                <th>{{ 'sys.approval.actedBy' | translate }}</th>
                <th>{{ 'sys.approval.actedAt' | translate }}</th>
                <th>{{ 'sys.approval.comment' | translate }}</th>
              </tr>
            </thead>
            <tbody>
              @for (step of stepsTable.data; track step.stepNo) {
                <tr [attr.data-testid]="'sys-approval-detail-step-' + step.stepNo">
                  <td>{{ step.stepNo }}</td>
                  <td>
                    {{ step.name }}
                    @if (step.minAmount !== null) {
                      <small>≥ {{ step.minAmount | number: '1.0-4' }}</small>
                    }
                  </td>
                  <td>
                    {{ 'sys.approval.approverValue' | translate: {
                      type: (approverTypeKey(step.approverType) | translate),
                      approver: step.roleName ?? step.username ?? '#' + (step.roleId ?? step.userId)
                    } }}
                  </td>
                  <td><nz-tag [nzColor]="statusColor(step.status)">{{ stepStatusKey(step.status) | translate }}</nz-tag></td>
                  <td>
                    {{ step.actedByUsername }}
                    @if (step.onBehalfOfUsername) {
                      <small>{{ 'sys.approval.onBehalfOf' | translate: { user: step.onBehalfOfUsername } }}</small>
                    }
                  </td>
                  <td>{{ displayTime(step.actedAt) }}</td>
                  <td>{{ step.comment }}</td>
                </tr>
              }
            </tbody>
          </nz-table>
        }
      </ng-container>
    </nz-modal>
  `,
  styles: `
    .erp-approval-inbox { display: grid; gap: var(--erp-space-2, 16px); color: var(--erp-color-text); }
    .erp-approval-inbox__header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 16px; }
    .erp-approval-inbox__header h1 { margin: 0; font-size: 20px; }
    .erp-approval-inbox__views { display: flex; gap: 8px; }
    .erp-approval-inbox__filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .erp-approval-inbox__filters input { max-width: 240px; }
    .erp-approval-inbox__filters nz-select { min-width: 160px; }
    .erp-approval-inbox__amount { text-align: right; font-variant-numeric: tabular-nums; }
    .erp-approval-inbox__error { color: var(--erp-color-error-text); margin: 4px 0 0; }
    .erp-approval-inbox__field { display: grid; gap: 4px; margin-bottom: 12px; }
    .erp-approval-inbox__required { color: var(--erp-color-error-text); }
    .erp-approval-inbox__count { justify-self: end; color: var(--erp-color-text-secondary, inherit); font-variant-numeric: tabular-nums; }
    .erp-approval-inbox__facts { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0 0 12px; }
    .erp-approval-inbox__facts dt { color: var(--erp-color-text-secondary, inherit); }
    .erp-approval-inbox__facts dd { margin: 0; }
    nz-alert, erp-api-error-display { display: block; margin-bottom: 12px; }
  `,
})
export class ApprovalInbox {
  readonly pageSizeOptions = [...PAGE_SIZE_OPTIONS];
  private readonly api = inject(ApprovalService);
  private readonly company = inject(CompanyContextService);
  private readonly permissions = inject(PermissionService);

  readonly statuses = APPROVAL_STATUSES;
  readonly approvalStatusKey = approvalStatusKey;
  readonly stepStatusKey = stepStatusKey;
  readonly approverTypeKey = approverTypeKey;
  readonly statusColor = statusColor;
  readonly displayTime = displayTime;
  readonly commentMaxLength = COMMENT_MAX_LENGTH;
  readonly overrideReasonMax = OVERRIDE_REASON_MAX_LENGTH;
  private readonly locale = inject(LOCALE_ID);
  private readonly injector = inject(Injector);
  private readonly document = inject(DOCUMENT);

  readonly canProcess = computed(() => this.permissions.has('SYS.APPROVAL.PROCESS'));
  readonly canView = computed(() => this.permissions.has('SYS.APPROVAL.VIEW'));
  /** 使用者選擇的頁籤；沒有選過或沒有該權限時，依權限決定（優先待辦）。 */
  private readonly chosenView = signal<ApprovalView | null>(null);
  readonly view = computed<ApprovalView>(() => {
    const chosen = this.chosenView();
    if (chosen === 'tasks' && this.canProcess()) return 'tasks';
    if (chosen === 'history' && this.canView()) return 'history';
    return this.canProcess() || !this.canView() ? 'tasks' : 'history';
  });

  readonly tasks = signal<ApprovalTask[]>([]);
  readonly history = signal<ApprovalInstanceSummary[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(20);
  /** 待辦預設舊到新（先處理等最久的），歷史預設新到舊。 */
  readonly sort = signal('submittedAt');
  readonly documentType = signal('');
  readonly keyword = signal('');
  readonly status = signal<ApprovalStatus | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly noticeParams = signal<Record<string, string>>({});

  readonly decision = signal<ApprovalDecisionDialog | null>(null);
  readonly comment = signal('');
  readonly commentMissing = signal(false);
  readonly deciding = signal(false);
  readonly decisionError = signal<string | null>(null);
  readonly decisionTraceId = signal<string | null>(null);
  /** 最近一次送出遇到 5xx／逾時／網絡錯誤：結果未明，不可再說「尚未保存」（SYS 會拒絕重複的決定）。 */
  readonly decisionUncertain = signal(false);
  /** 伺服器拒絕核准並要求覆核理由（超額）；對話框重新開啟或遇到不可覆核的拒絕時清除。 */
  readonly creditOverride = signal<ApprovalCreditOverride | null>(null);
  readonly overrideReason = signal('');
  readonly overrideReasonError = signal<string | null>(null);
  /** 需要覆核但本人沒有覆核權限：停用核准並說明原因（伺服器仍會檢查）。 */
  readonly overrideBlocked = computed(() => {
    const over = this.creditOverride();
    return over !== null && this.decision()?.action === 'approve' && !this.permissions.has(over.permission);
  });

  readonly detailOpen = signal(false);
  readonly detail = signal<ApprovalInstanceDetail | null>(null);
  readonly detailError = signal<string | null>(null);

  readonly decisionTitle = computed(() => (this.decision()?.action === 'reject' ? 'sys.approval.rejectTitle' : 'sys.approval.approveTitle'));
  readonly decisionOkText = computed(() => (this.decision()?.action === 'reject' ? 'sys.approval.reject' : 'sys.approval.approve'));
  readonly decisionParams = computed(() => ({ no: this.decision()?.task.documentNo ?? '' }));
  /** 明細標題參數：nz-modal 開啟後不會更新標題，所以開啟前由列資料設定。 */
  readonly detailParams = signal({ type: '', no: '' });

  /** 請求序號：切換頁籤或公司後，忽略較早的回應。 */
  private loadSeq = 0;
  private detailSeq = 0;
  private readonly companyId = computed(() => this.company.current()?.id ?? null);

  constructor() {
    // 待辦與歷史屬於目前公司；權限載入或切換公司後回到第一頁重新查詢
    effect(() => {
      this.companyId();
      this.view();
      untracked(() => {
        this.page.set(1);
        this.sort.set(this.view() === 'tasks' ? 'submittedAt' : '-submittedAt');
        this.closeDecision();
        this.closeDetail();
        this.reload();
      });
    });
  }

  switchView(view: ApprovalView): void {
    if (view === this.view()) return;
    this.notice.set(null);
    this.chosenView.set(view);
  }

  sortOrder(): 'ascend' | 'descend' {
    return this.sort() === 'submittedAt' ? 'ascend' : 'descend';
  }

  search(): void {
    this.page.set(1);
    this.reload();
  }

  onQueryParams(params: NzTableQueryParams): void {
    const sorted = params.sort.find((s) => s.key === 'submittedAt' && s.value !== null);
    const fallback = this.view() === 'tasks' ? 'submittedAt' : '-submittedAt';
    const sort = sorted ? (sorted.value === 'ascend' ? 'submittedAt' : '-submittedAt') : fallback;
    if (params.pageIndex === this.page() && params.pageSize === this.pageSize() && sort === this.sort()) return;
    this.page.set(params.pageIndex);
    this.pageSize.set(params.pageSize);
    this.sort.set(sort);
    this.reload();
  }

  reload(): void {
    const view = this.view();
    if (view === 'tasks' ? !this.canProcess() : !this.canView()) {
      this.tasks.set([]);
      this.history.set([]);
      this.total.set(0);
      return;
    }
    const seq = ++this.loadSeq;
    const query = {
      page: this.page(),
      pageSize: this.pageSize(),
      documentType: this.documentType(),
      keyword: this.keyword(),
      sort: this.sort(),
    };
    this.loading.set(true);
    this.loadError.set(null);
    const done = (total: number) => {
      this.total.set(total);
      this.loading.set(false);
    };
    const failed = (error: unknown) => {
      if (seq !== this.loadSeq) return;
      this.tasks.set([]);
      this.history.set([]);
      this.loadError.set(approvalErrorKey(error));
      done(0);
    };
    if (view === 'tasks') {
      this.api.tasks(query).subscribe({
        next: (result) => {
          if (seq !== this.loadSeq) return;
          this.tasks.set(result.items);
          done(result.totalCount);
        },
        error: failed,
      });
    } else {
      this.api.history({ ...query, status: this.status() }).subscribe({
        next: (result) => {
          if (seq !== this.loadSeq) return;
          this.history.set(result.items);
          done(result.totalCount);
        },
        error: failed,
      });
    }
  }

  openDecision(action: 'approve' | 'reject', task: ApprovalTask): void {
    this.comment.set('');
    this.commentMissing.set(false);
    this.resetDecisionState();
    this.decision.set({ action, task });
  }

  closeDecision(): void {
    this.decision.set(null);
    this.commentMissing.set(false);
    this.resetDecisionState();
  }

  private resetDecisionState(): void {
    this.decisionError.set(null);
    this.decisionTraceId.set(null);
    this.decisionUncertain.set(false);
    this.creditOverride.set(null);
    this.overrideReason.set('');
    this.overrideReasonError.set(null);
  }

  submitDecision(): void {
    const current = this.decision();
    if (!current || this.deciding()) return;
    const comment = this.comment().trim();
    if (current.action === 'reject' && !comment) {
      this.commentMissing.set(true);
      return;
    }
    this.commentMissing.set(false);
    // 需要覆核時，理由在前端先檢查（去除前後空白後 1–200 字），不合格不送出
    const over = current.action === 'approve' ? this.creditOverride() : null;
    if (over && this.overrideBlocked()) return;
    const reason = over ? this.overrideReason().trim() : undefined;
    if (reason !== undefined && (!reason || reason.length > this.overrideReasonMax)) {
      this.overrideReasonError.set(reason ? 'sys.approval.credit.reasonTooLong' : 'sys.approval.credit.reasonRequired');
      this.focusOverrideReason();
      return;
    }
    this.overrideReasonError.set(null);
    const { task } = current;
    const request =
      current.action === 'approve'
        ? this.api.approve(task.instanceId, comment || null, task.version, reason)
        : this.api.reject(task.instanceId, comment, task.version);
    this.deciding.set(true);
    this.decisionError.set(null);
    this.decisionTraceId.set(null);
    this.decisionUncertain.set(false);
    request.subscribe({
      next: (result) => {
        this.deciding.set(false);
        // 有送出理由且單據已最終核准 → 說明已覆核的超額；其他情況照一般提示
        if (over && result.status === 'Approved') {
          this.noticeParams.set({ no: task.documentNo, amount: formatNumber(over.excess, this.locale, '1.0-4'), currency: over.currency ?? '' });
          this.notice.set('sys.approval.approvedWithOverride');
        } else {
          this.noticeParams.set({ no: task.documentNo });
          // 核准後仍為審批中 → 已送往下一步
          this.notice.set(
            current.action === 'reject' ? 'sys.approval.rejected' : result.status === 'Pending' ? 'sys.approval.forwarded' : 'sys.approval.approved',
          );
        }
        this.closeDecision();
        this.reload();
      },
      error: (error: unknown) => {
        this.deciding.set(false);
        this.failDecision(current.action, error);
      },
    });
  }

  /** 決定失敗：可覆核的超額改顯示理由欄；理由欄位錯誤標在欄位下；其他錯誤（含 5xx 追蹤碼）顯示在對話框頂部。 */
  private failDecision(action: 'approve' | 'reject', error: unknown): void {
    this.decisionUncertain.set(!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500);
    const refusal = action === 'approve' ? overridableRefusal(error) : null;
    if (refusal) {
      this.creditOverride.set({ excess: refusal.excess, currency: refusal.currency, permission: OVERRIDABLE_REFUSALS[refusal.code] });
      this.focusOverrideReason();
      return;
    }
    if (this.creditOverride() && isOverrideReasonError(error)) {
      this.overrideReasonError.set('sys.approval.credit.reasonInvalid');
      this.focusOverrideReason();
      return;
    }
    const key = approvalErrorKey(error);
    // 不可覆核的信用拒絕（受阻信用等）：移除理由欄，只留原因
    if (key.startsWith('errors.SAL.CREDIT.') && key !== 'errors.SAL.CREDIT.OVERRIDE_FORBIDDEN') this.creditOverride.set(null);
    if (key === 'errors.SAL.ORDER.BASE_CURRENCY_CHANGED') this.creditOverride.set(null);
    this.decisionError.set(key);
    this.decisionTraceId.set(apiErrorTraceId(error));
  }

  /** 理由欄出現或有錯誤時移到該欄（對話框內容在 CDK overlay，渲染後才存在）。 */
  private focusOverrideReason(): void {
    afterNextRender({ write: () => (this.document.getElementById('approval-override-reason') as HTMLTextAreaElement | null)?.focus() }, { injector: this.injector });
  }

  openDetail(row: { documentType: string; documentNo: string }, id: number): void {
    const seq = ++this.detailSeq;
    this.detailParams.set({ type: row.documentType, no: row.documentNo });
    this.detail.set(null);
    this.detailError.set(null);
    this.detailOpen.set(true);
    this.api.get(id).subscribe({
      next: (result) => {
        if (seq === this.detailSeq) this.detail.set(result);
      },
      error: (error: unknown) => {
        if (seq === this.detailSeq) this.detailError.set(approvalErrorKey(error));
      },
    });
  }

  closeDetail(): void {
    this.detailSeq++;
    this.detailOpen.set(false);
    this.detail.set(null);
    this.detailError.set(null);
  }
}
