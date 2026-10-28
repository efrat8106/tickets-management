import { DatePipe } from '@angular/common';
import { AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, input, output, viewChild } from '@angular/core';
import { PagedResult, Ticket, TicketHistoryItem } from '../../../core/models/ticket.models';
import { STATUS_LABELS } from '../ticket-labels';
import { LoadState } from '../tickets.store';

/**
 * Presentational: status history of one ticket in a native modal dialog
 * (focus trap, Esc to close and inert background come from the browser).
 */
@Component({
  selector: 'app-ticket-history',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  template: `
    <dialog #dialog aria-labelledby="history-title" (close)="close.emit()">
      <header>
        <h2 id="history-title">היסטוריית סטטוס – פנייה {{ ticket().id }}</h2>
        <button type="button" (click)="dialog.close()" aria-label="סגירה">✕</button>
      </header>
      <p class="meta">{{ ticket().title }} · {{ ticket().organizationName }}</p>

      @switch (state().status) {
        @case ('loading') {
          <p>טוען היסטוריה…</p>
        }
        @case ('error') {
          <div class="message error" role="alert">
            טעינת ההיסטוריה נכשלה: {{ state().error?.displayMessage }}
            <button type="button" (click)="pageChange.emit(state().data?.page ?? 1)">נסו שוב</button>
          </div>
        }
      }

      @if (state().data; as history) {
        @if (history.totalCount === 0) {
          <p>אין שינויי סטטוס מתועדים לפנייה זו.</p>
        } @else {
          <table>
            <thead>
              <tr><th scope="col">מועד</th><th scope="col">מ-</th><th scope="col">אל</th><th scope="col">שונה על ידי</th></tr>
            </thead>
            <tbody>
              @for (item of history.items; track item.id) {
                <tr>
                  <td>{{ item.changedAt | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
                  <td>{{ labels[item.previousStatus] }}</td>
                  <td>{{ labels[item.newStatus] }}</td>
                  <td>{{ item.changedBy }}</td>
                </tr>
              }
            </tbody>
          </table>
          @if (history.totalPages > 1) {
            <nav class="pager" aria-label="דפדוף בהיסטוריה">
              <button type="button" [disabled]="history.page <= 1" (click)="pageChange.emit(history.page - 1)">החדשים יותר</button>
              <span>עמוד {{ history.page }} מתוך {{ history.totalPages }}</span>
              <button type="button" [disabled]="history.page >= history.totalPages" (click)="pageChange.emit(history.page + 1)">הישנים יותר</button>
            </nav>
          }
        }
      }
    </dialog>
  `,
  styles: `
    dialog { width: min(40rem, calc(100vw - 2rem)); border: 1px solid var(--border); border-radius: 6px; padding: 1rem; }
    dialog::backdrop { background: rgb(0 0 0 / 0.35); }
    header { display: flex; justify-content: space-between; align-items: center; gap: 1rem; }
    h2 { margin: 0; font-size: 1.05rem; }
    .meta { color: var(--muted); margin: 0.25rem 0 0.75rem; }
    table { width: 100%; border-collapse: collapse; font-size: 0.875rem; }
    th, td { padding: 0.35rem 0.5rem; border-bottom: 1px solid var(--border); text-align: start; }
    .pager { display: flex; align-items: center; gap: 0.75rem; margin-top: 0.75rem; }
  `,
})
export class TicketHistoryComponent implements AfterViewInit {
  readonly ticket = input.required<Ticket>();
  readonly state = input.required<LoadState<PagedResult<TicketHistoryItem>>>();
  readonly close = output<void>();
  readonly pageChange = output<number>();

  protected readonly labels = STATUS_LABELS;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  ngAfterViewInit(): void {
    this.dialog().nativeElement.showModal();
  }
}
