import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { SortField } from '../../../core/models/ticket.models';
import { TicketFiltersComponent } from '../ticket-filters/ticket-filters.component';
import { TicketSummaryComponent } from '../ticket-summary/ticket-summary.component';
import { PRIORITY_LABELS, SORT_LABELS, STATUS_LABELS } from '../ticket-labels';
import { TicketsStore } from '../tickets.store';

/** The tickets screen: connects the store to the presentational components. */
@Component({
  selector: 'app-ticket-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, TicketFiltersComponent, TicketSummaryComponent],
  providers: [TicketsStore],
  templateUrl: './ticket-list.component.html',
  styleUrl: './ticket-list.component.css',
})
export class TicketListComponent implements OnInit {
  protected readonly store = inject(TicketsStore);

  protected readonly statusLabels = STATUS_LABELS;
  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly sortLabels = SORT_LABELS;
  protected readonly columns: SortField[] = ['title', 'organizationName', 'status', 'priority', 'createdAt', 'updatedAt'];
  protected readonly pageSizes = [10, 20, 50, 100];

  protected readonly page = computed(() => this.store.list().data);
  protected readonly isLoading = computed(() => this.store.list().status === 'loading');
  protected readonly isEmpty = computed(() => this.store.list().status === 'success' && this.page()?.totalCount === 0);

  ngOnInit(): void {
    this.store.loadSummary();
  }

  protected ariaSort(field: SortField): 'ascending' | 'descending' | 'none' {
    const query = this.store.query();
    return query.sortBy !== field ? 'none' : query.sortDir === 'asc' ? 'ascending' : 'descending';
  }

  protected sortIndicator(field: SortField): string {
    const sort = this.ariaSort(field);
    return sort === 'ascending' ? '▲' : sort === 'descending' ? '▼' : '';
  }

  protected pageSizeChanged(event: Event): void {
    this.store.setPageSize(Number((event.target as HTMLSelectElement).value));
  }
}
