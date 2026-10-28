import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import {
  TICKET_PRIORITIES,
  TICKET_STATUSES,
  TicketPriority,
  TicketQuery,
  TicketStatus,
} from '../../../core/models/ticket.models';
import { PRIORITY_LABELS, STATUS_LABELS } from '../ticket-labels';

export type FilterChange = Partial<Pick<TicketQuery, 'status' | 'priority' | 'organizationName' | 'assignedTo' | 'createdFrom' | 'createdTo'>>;

/** Presentational: renders the current query and emits changes. Debouncing is done by the store. */
@Component({
  selector: 'app-ticket-filters',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './ticket-filters.component.html',
  styleUrl: './ticket-filters.component.css',
})
export class TicketFiltersComponent {
  readonly query = input.required<TicketQuery>();
  readonly hasActiveFilters = input(false);

  readonly searchChange = output<string>();
  readonly filtersChange = output<FilterChange>();
  readonly clear = output<void>();

  protected readonly statuses = TICKET_STATUSES;
  protected readonly priorities = TICKET_PRIORITIES;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly priorityLabels = PRIORITY_LABELS;

  protected toggleStatus(status: TicketStatus, checked: boolean): void {
    this.filtersChange.emit({ status: toggle(this.query().status, status, checked) });
  }

  protected togglePriority(priority: TicketPriority, checked: boolean): void {
    this.filtersChange.emit({ priority: toggle(this.query().priority, priority, checked) });
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }
}

function toggle<T>(values: readonly T[], value: T, include: boolean): T[] {
  return include ? [...values.filter((v) => v !== value), value] : values.filter((v) => v !== value);
}
