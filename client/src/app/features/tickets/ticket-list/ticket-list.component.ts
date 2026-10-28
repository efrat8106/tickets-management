import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';
import { SortField } from '../../../core/models/ticket.models';
import { BulkUpdateBarComponent } from '../status-update/bulk-update-bar.component';
import { StatusSelectComponent } from '../status-update/status-select.component';
import { TicketFiltersComponent } from '../ticket-filters/ticket-filters.component';
import { TicketHistoryComponent } from '../ticket-history/ticket-history.component';
import { TicketSummaryComponent } from '../ticket-summary/ticket-summary.component';
import { PRIORITY_LABELS, SORT_LABELS, STATUS_LABELS } from '../ticket-labels';
import { MAX_BULK_ITEMS, TicketsStore } from '../tickets.store';

/** The tickets screen: connects the store to the presentational components. */
@Component({
  selector: 'app-ticket-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DatePipe,
    TicketFiltersComponent,
    TicketSummaryComponent,
    StatusSelectComponent,
    BulkUpdateBarComponent,
    TicketHistoryComponent,
  ],
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

  protected readonly maxBulk = MAX_BULK_ITEMS;

  protected readonly page = computed(() => this.store.list().data);
  protected readonly allOnPageSelected = computed(() => {
    const items = this.page()?.items ?? [];
    const selection = this.store.selection();
    return items.length > 0 && items.every((t) => selection.has(t.id));
  });
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

  protected unselectPage(): void {
    for (const ticket of this.page()?.items ?? []) {
      if (this.store.selection().has(ticket.id)) {
        this.store.toggleSelection(ticket);
      }
    }
  }

  protected pageSizeChanged(event: Event): void {
    this.store.setPageSize(Number((event.target as HTMLSelectElement).value));
  }
}
