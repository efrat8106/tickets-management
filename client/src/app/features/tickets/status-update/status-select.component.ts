import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TicketStatus } from '../../../core/models/ticket.models';
import { ALLOWED_TRANSITIONS, STATUS_LABELS } from '../ticket-labels';

/** Presentational: offers the statuses reachable from the current one and emits the chosen status. */
@Component({
  selector: 'app-status-select',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <select [attr.aria-label]="label()" [disabled]="disabled() || options().length === 0" (change)="choose($event)">
      <option value="" selected>שינוי סטטוס…</option>
      @for (status of options(); track status) {
        <option [value]="status">{{ labels[status] }}</option>
      }
    </select>
  `,
})
export class StatusSelectComponent {
  readonly status = input.required<TicketStatus>();
  readonly label = input('שינוי סטטוס');
  readonly disabled = input(false);
  readonly statusChange = output<TicketStatus>();

  protected readonly labels = STATUS_LABELS;
  protected readonly options = computed(() => ALLOWED_TRANSITIONS[this.status()]);

  protected choose(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const value = select.value as TicketStatus | '';
    select.value = ''; // back to the placeholder; the row shows the new status once the server confirms it
    if (value) {
      this.statusChange.emit(value);
    }
  }
}
