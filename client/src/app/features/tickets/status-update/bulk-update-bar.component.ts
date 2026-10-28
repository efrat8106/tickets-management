import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { BulkUpdateResponse, StatusUpdateOutcome, TICKET_STATUSES, TicketStatus } from '../../../core/models/ticket.models';
import { STATUS_LABELS } from '../ticket-labels';

const OUTCOME_LABELS: Record<StatusUpdateOutcome, string> = {
  Updated: 'עודכנה',
  NotFound: 'לא נמצאה',
  InvalidTransition: 'מעבר סטטוס לא מותר',
  Conflict: 'עודכנה בינתיים על ידי משתמש אחר',
};

/** Presentational: bulk status change for the selected tickets and the per-item result. */
@Component({
  selector: 'app-bulk-update-bar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (selectedCount() > 0 || result() || error()) {
      <section class="bulk card" aria-label="עדכון מרוכז">
        <div class="row">
          <strong>{{ selectedCount() }} נבחרו</strong>
          <span class="meta">(עד {{ max() }})</span>
          @if (selectedCount() >= max()) {
            <span class="message warning compact">הגעתם למספר המרבי של פניות לעדכון מרוכז.</span>
          }
          <label>
            סטטוס חדש
            <select [value]="target()" (change)="target.set($any($event.target).value)">
              @for (status of statuses; track status) {
                <option [value]="status">{{ statusLabels[status] }}</option>
              }
            </select>
          </label>
          <button type="button" class="primary" [disabled]="busy() || selectedCount() === 0" (click)="apply.emit(target())">
            {{ busy() ? 'מעדכן…' : 'עדכון הנבחרות' }}
          </button>
          <button type="button" [disabled]="busy() || selectedCount() === 0" (click)="clearSelection.emit()">ביטול בחירה</button>
        </div>

        @if (error(); as message) {
          <div class="message error" role="alert">העדכון המרוכז נכשל: {{ message }}</div>
        }

        @if (result(); as r) {
          <div class="result" role="status">
            <p>
              <span class="ok">{{ r.succeeded }} עודכנו</span>
              @if (r.failed > 0) {
                · <span class="failed">{{ r.failed }} נכשלו</span>
                (הפניות שנכשלו נשארו מסומנות עם הנתונים העדכניים, וניתן לנסות שוב)
              }
              <button type="button" class="link" (click)="dismiss.emit()">סגירה</button>
            </p>
            @if (failures().length > 0) {
              <ul>
                @for (item of failures(); track item.id) {
                  <li>פנייה {{ item.id }}: {{ outcomeLabels[item.outcome] }}@if (item.message) { – {{ item.message }} }</li>
                }
              </ul>
            }
          </div>
        }
      </section>
    }
  `,
  styles: `
    .bulk { display: flex; flex-direction: column; gap: 0.5rem; border-color: var(--accent); }
    .row { display: flex; flex-wrap: wrap; align-items: center; gap: 0.75rem; }
    label { display: flex; align-items: center; gap: 0.4rem; }
    .meta { color: var(--muted); font-size: 0.85rem; }
    .compact { padding: 0.15rem 0.5rem; font-size: 0.85rem; }
    .result p { margin: 0; }
    .ok { color: var(--success); font-weight: 600; }
    .failed { color: var(--danger); font-weight: 600; }
    ul { margin: 0.4rem 0 0; padding-inline-start: 1.25rem; font-size: 0.85rem; }
    .link { border: 0; background: none; color: var(--accent); text-decoration: underline; margin-inline-start: 0.5rem; }
  `,
})
export class BulkUpdateBarComponent {
  readonly selectedCount = input.required<number>();
  readonly max = input.required<number>();
  readonly busy = input(false);
  readonly result = input<BulkUpdateResponse | null>(null);
  readonly error = input<string | null>(null);

  readonly apply = output<TicketStatus>();
  readonly clearSelection = output<void>();
  readonly dismiss = output<void>();

  protected readonly statuses = TICKET_STATUSES;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly outcomeLabels = OUTCOME_LABELS;
  protected readonly target = signal<TicketStatus>('InProgress');
  protected readonly failures = computed(() => this.result()?.results.filter((r) => r.outcome !== 'Updated') ?? []);
}
