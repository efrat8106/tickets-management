import { DatePipe, DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TicketSummary } from '../../../core/models/ticket.models';
import { LoadState } from '../tickets.store';
import { PRIORITY_LABELS, STATUS_LABELS } from '../ticket-labels';

/** Presentational: aggregated counts computed by the server. */
@Component({
  selector: 'app-ticket-summary',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, DecimalPipe],
  template: `
    <section class="summary card" aria-label="סיכום">
      <header>
        <h2>סיכום</h2>
        @if (state().data; as s) {
          <span class="meta">{{ s.totalCount | number }} פניות · עודכן {{ s.generatedAt | date: 'HH:mm:ss' }}</span>
        }
        @if (state().status === 'loading') {
          <span class="meta">טוען…</span>
        }
      </header>

      @if (state().status === 'error') {
        <div class="message error" role="alert">
          טעינת הסיכום נכשלה: {{ state().error?.displayMessage }}
          <button type="button" (click)="retry.emit()">נסו שוב</button>
        </div>
      }

      @if (state().data; as s) {
        <div class="groups">
          <div>
            <h3>לפי סטטוס</h3>
            <ul class="tiles">
              @for (item of s.byStatus; track item.status) {
                <li><span class="value">{{ item.count | number }}</span><span class="label">{{ statusLabels[item.status] }}</span></li>
              }
            </ul>
          </div>
          <div>
            <h3>לפי עדיפות</h3>
            <ul class="tiles">
              @for (item of s.byPriority; track item.priority) {
                <li><span class="value">{{ item.count | number }}</span><span class="label">{{ priorityLabels[item.priority] }}</span></li>
              }
            </ul>
          </div>
          <div>
            <h3>מטפלים עם הכי הרבה פניות פתוחות</h3>
            @if (s.topAssigneesByOpenTickets.length === 0) {
              <p class="meta">אין פניות פתוחות משויכות.</p>
            } @else {
              <ol class="assignees">
                @for (item of s.topAssigneesByOpenTickets; track item.assignedTo) {
                  <li><span>{{ item.assignedTo }}</span><span>{{ item.openCount | number }}</span></li>
                }
              </ol>
            }
          </div>
        </div>
      }
    </section>
  `,
  styles: `
    header { display: flex; align-items: baseline; gap: 0.75rem; flex-wrap: wrap; }
    h2 { margin: 0 0 0.5rem; font-size: 1.05rem; }
    h3 { margin: 0 0 0.4rem; font-size: 0.875rem; color: var(--muted); font-weight: 600; }
    .meta { color: var(--muted); font-size: 0.8rem; }
    .groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(15rem, 1fr)); gap: 1rem; }
    .tiles { display: flex; flex-wrap: wrap; gap: 0.5rem; list-style: none; margin: 0; padding: 0; }
    .tiles li { display: flex; flex-direction: column; min-width: 5.5rem; padding: 0.4rem 0.6rem; border: 1px solid var(--border); border-radius: 4px; }
    .value { font-size: 1.1rem; font-weight: 600; }
    .label { font-size: 0.8rem; color: var(--muted); }
    .assignees { margin: 0; padding-inline-start: 1.25rem; font-size: 0.85rem; columns: 2; column-gap: 1.5rem; }
    .assignees li span:last-child { margin-inline-start: 0.5rem; color: var(--muted); }
  `,
})
export class TicketSummaryComponent {
  readonly state = input.required<LoadState<TicketSummary>>();
  readonly retry = output<void>();

  protected readonly statusLabels = STATUS_LABELS;
  protected readonly priorityLabels = PRIORITY_LABELS;
}
