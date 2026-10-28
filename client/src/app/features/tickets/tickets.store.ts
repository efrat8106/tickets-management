import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  BehaviorSubject,
  Observable,
  Subject,
  catchError,
  debounceTime,
  filter,
  finalize,
  map,
  merge,
  of,
  startWith,
  switchMap,
} from 'rxjs';
import { TicketsApiService } from '../../core/api/tickets-api.service';
import { ApiError } from '../../core/http/api-error';
import {
  BulkUpdateResponse,
  PagedResult,
  SortField,
  Ticket,
  TicketHistoryItem,
  TicketQuery,
  TicketStatus,
  TicketSummary,
} from '../../core/models/ticket.models';

export interface LoadState<T> {
  status: 'idle' | 'loading' | 'success' | 'error';
  /** Last successful data; kept while a new request is loading, so the screen does not flicker. */
  data: T | null;
  error: ApiError | null;
}

export interface RowMessage {
  kind: 'success' | 'warning' | 'error';
  text: string;
}

export const SEARCH_DEBOUNCE_MS = 300;
export const MAX_BULK_ITEMS = 100;
const HISTORY_PAGE_SIZE = 20;

export const DEFAULT_QUERY: TicketQuery = {
  page: 1,
  pageSize: 20,
  status: [],
  priority: [],
  organizationName: '',
  assignedTo: '',
  createdFrom: '',
  createdTo: '',
  search: '',
  sortBy: 'createdAt',
  sortDir: 'desc',
};

type FilterChanges = Partial<Pick<TicketQuery, 'status' | 'priority' | 'organizationName' | 'assignedTo' | 'createdFrom' | 'createdTo'>>;

/**
 * State and behavior of the tickets screen (provided per screen). Components only render signals and call methods;
 * all HTTP goes through {@link TicketsApiService}, and all filtering, sorting and paging is done by the server.
 */
@Injectable()
export class TicketsStore {
  private readonly api = inject(TicketsApiService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly query$ = new BehaviorSubject<TicketQuery>(DEFAULT_QUERY);
  private readonly reload$ = new Subject<void>();
  private readonly searchInput$ = new Subject<string>();
  private readonly historyRequest$ = new Subject<{ ticketId: number; page: number }>();
  private readonly summaryRequest$ = new Subject<void>();

  readonly query = signal<TicketQuery>(DEFAULT_QUERY);
  readonly list = signal<LoadState<PagedResult<Ticket>>>({ status: 'loading', data: null, error: null });
  readonly summary = signal<LoadState<TicketSummary>>({ status: 'idle', data: null, error: null });
  readonly rowMessages = signal<ReadonlyMap<number, RowMessage>>(new Map());
  readonly busyRows = signal<ReadonlySet<number>>(new Set());

  /** Selected tickets with the RowVersion last seen for each (needed for bulk updates). */
  readonly selection = signal<ReadonlyMap<number, Ticket>>(new Map());
  readonly selectionFull = computed(() => this.selection().size >= MAX_BULK_ITEMS);
  readonly bulkBusy = signal(false);
  readonly bulkResult = signal<BulkUpdateResponse | null>(null);
  readonly bulkError = signal<string | null>(null);

  readonly historyTicket = signal<Ticket | null>(null);
  readonly history = signal<LoadState<PagedResult<TicketHistoryItem>>>({ status: 'idle', data: null, error: null });

  readonly hasActiveFilters = computed(() => {
    const q = this.query();
    return q.status.length > 0 || q.priority.length > 0 || !!q.organizationName || !!q.assignedTo ||
      !!q.createdFrom || !!q.createdTo || !!q.search;
  });

  constructor() {
    // Search box: wait for a 300 ms pause in typing, then apply. The value is compared with the current query
    // (not with the previous keystroke, as distinctUntilChanged would) so that typing the same text again after
    // "clear filters" still triggers a search.
    this.searchInput$
      .pipe(
        map((text) => text.trim()),
        debounceTime(SEARCH_DEBOUNCE_MS),
        filter((search) => search !== this.query$.value.search),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((search) => this.updateQuery({ search }));

    // Every query change (or retry) starts a request; switchMap unsubscribes from the previous one, which cancels
    // the in-flight HTTP request – a slow, outdated response can never overwrite a newer one.
    merge(this.query$, this.reload$.pipe(map(() => this.query$.value)))
      .pipe(
        switchMap((query) => this.load(this.api.getTickets(query), () => this.list().data)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.list.set(state));

    // Summary is reloaded after every update; switchMap keeps only the latest response.
    this.summaryRequest$
      .pipe(
        switchMap(() => this.load(this.api.getSummary(), () => this.summary().data)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.summary.set(state));

    this.historyRequest$
      .pipe(
        switchMap(({ ticketId, page }) =>
          this.load(this.api.getHistory(ticketId, page, HISTORY_PAGE_SIZE), () => this.history().data),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => this.history.set(state));
  }

  // ---- query ----

  /** Called on every keystroke; the request is sent after the debounce. */
  search(text: string): void {
    this.searchInput$.next(text);
  }

  setFilters(changes: FilterChanges): void {
    this.updateQuery(changes);
  }

  clearFilters(): void {
    const { page, status, priority, organizationName, assignedTo, createdFrom, createdTo, search } = DEFAULT_QUERY;
    this.updateQuery({ page, status, priority, organizationName, assignedTo, createdFrom, createdTo, search });
  }

  /** Same column toggles the direction; a new column starts descending. */
  sortBy(field: SortField): void {
    const current = this.query$.value;
    const sortDir = current.sortBy === field && current.sortDir === 'desc' ? 'asc' : 'desc';
    this.updateQuery({ sortBy: field, sortDir });
  }

  goToPage(page: number): void {
    this.updateQuery({ page }, false);
  }

  setPageSize(pageSize: number): void {
    this.updateQuery({ pageSize });
  }

  retry(): void {
    this.reload$.next();
  }

  // ---- summary ----

  loadSummary(): void {
    this.summaryRequest$.next();
  }

  // ---- single status update ----

  updateStatus(ticket: Ticket, newStatus: TicketStatus): void {
    this.setBusy(ticket.id, true);
    this.clearRowMessage(ticket.id);

    this.api
      .updateStatus(ticket.id, newStatus, ticket.rowVersion)
      .pipe(
        finalize(() => this.setBusy(ticket.id, false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (updated) => {
          this.replaceTicket(updated);
          this.setRowMessage(ticket.id, { kind: 'success', text: 'הסטטוס עודכן.' });
          this.loadSummary();
        },
        error: (error: ApiError) => this.handleUpdateError(ticket, error),
      });
  }

  private handleUpdateError(ticket: Ticket, error: ApiError): void {
    switch (error.status) {
      case 409: {
        // Someone else changed the ticket. The response carries its current state: show it right away, so the
        // user decides again based on fresh data (and the next attempt uses the new RowVersion).
        const current = error.extension<Ticket>('currentTicket');
        if (current) {
          this.replaceTicket(current);
        } else {
          this.refreshTicket(ticket.id);
        }
        this.setRowMessage(ticket.id, {
          kind: 'warning',
          text: 'הפנייה עודכנה על ידי משתמש אחר. השורה רועננה לנתונים העדכניים – בדקו ונסו שוב.',
        });
        break;
      }
      case 404:
        this.setRowMessage(ticket.id, { kind: 'error', text: 'הפנייה לא נמצאה (ייתכן שנמחקה).' });
        break;
      default:
        this.setRowMessage(ticket.id, { kind: 'error', text: error.displayMessage });
    }
  }

  /** Reloads one row from the server (used when a 409 did not include the current ticket). */
  refreshTicket(id: number): void {
    this.api
      .getTicket(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (ticket) => this.replaceTicket(ticket), error: () => undefined });
  }

  // ---- selection & bulk ----

  toggleSelection(ticket: Ticket): void {
    const next = new Map(this.selection());
    if (next.has(ticket.id)) {
      next.delete(ticket.id);
    } else if (next.size < MAX_BULK_ITEMS) {
      next.set(ticket.id, ticket);
    }
    this.selection.set(next);
  }

  /** Selects all rows of the current page, up to the bulk limit. */
  selectPage(): void {
    const next = new Map(this.selection());
    for (const ticket of this.list().data?.items ?? []) {
      if (next.size >= MAX_BULK_ITEMS) {
        break;
      }
      next.set(ticket.id, ticket);
    }
    this.selection.set(next);
  }

  clearSelection(): void {
    this.selection.set(new Map());
  }

  bulkUpdate(newStatus: TicketStatus): void {
    const items = [...this.selection().values()].map((t) => ({ id: t.id, rowVersion: t.rowVersion }));
    if (items.length === 0) {
      return;
    }

    this.bulkBusy.set(true);
    this.bulkError.set(null);
    this.bulkResult.set(null);

    this.api
      .bulkUpdateStatus(newStatus, items)
      .pipe(
        finalize(() => this.bulkBusy.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (response) => {
          const selection = new Map(this.selection());
          for (const result of response.results) {
            if (result.ticket) {
              this.replaceTicket(result.ticket);
            }
            if (result.outcome === 'Updated' || result.outcome === 'NotFound') {
              selection.delete(result.id); // done; failed items stay selected (with fresh RowVersion) for a retry
            }
          }
          this.selection.set(selection);
          this.bulkResult.set(response);
          if (response.succeeded > 0) {
            this.loadSummary();
          }
        },
        error: (error: ApiError) => this.bulkError.set(error.displayMessage),
      });
  }

  dismissBulkResult(): void {
    this.bulkResult.set(null);
    this.bulkError.set(null);
  }

  // ---- history ----

  openHistory(ticket: Ticket): void {
    this.historyTicket.set(ticket);
    this.history.set({ status: 'loading', data: null, error: null });
    this.historyRequest$.next({ ticketId: ticket.id, page: 1 });
  }

  historyPage(page: number): void {
    const ticket = this.historyTicket();
    if (ticket) {
      this.historyRequest$.next({ ticketId: ticket.id, page });
    }
  }

  closeHistory(): void {
    this.historyTicket.set(null);
  }

  // ---- helpers ----

  private updateQuery(changes: Partial<TicketQuery>, resetPage = true): void {
    const next = { ...this.query$.value, ...changes };
    if (resetPage && changes.page === undefined) {
      next.page = 1; // any filter / sort / page size change starts from the first page
    }
    this.query.set(next);
    this.query$.next(next);
  }

  /** Wraps a request into loading → success | error states. */
  private load<T>(request: Observable<T>, previous: () => T | null): Observable<LoadState<T>> {
    return request.pipe(
      map((data): LoadState<T> => ({ status: 'success', data, error: null })),
      catchError((error: unknown) =>
        of<LoadState<T>>({ status: 'error', data: previous(), error: toApiError(error) }),
      ),
      startWith<LoadState<T>>({ status: 'loading', data: previous(), error: null }),
    );
  }

  /** Puts the server's version of a ticket into the list and the selection (fresh status and RowVersion). */
  private replaceTicket(ticket: Ticket): void {
    this.list.update((state) =>
      state.data
        ? { ...state, data: { ...state.data, items: state.data.items.map((t) => (t.id === ticket.id ? ticket : t)) } }
        : state,
    );
    if (this.selection().has(ticket.id)) {
      this.selection.update((selection) => new Map(selection).set(ticket.id, ticket));
    }
  }

  private setBusy(id: number, busy: boolean): void {
    this.busyRows.update((rows) => {
      const next = new Set(rows);
      if (busy) {
        next.add(id);
      } else {
        next.delete(id);
      }
      return next;
    });
  }

  private setRowMessage(id: number, message: RowMessage): void {
    this.rowMessages.update((messages) => new Map(messages).set(id, message));
  }

  private clearRowMessage(id: number): void {
    this.rowMessages.update((messages) => {
      const next = new Map(messages);
      next.delete(id);
      return next;
    });
  }
}

function toApiError(error: unknown): ApiError {
  return error instanceof ApiError ? error : new ApiError(0, 'שגיאה לא צפויה', String(error), {}, null);
}
