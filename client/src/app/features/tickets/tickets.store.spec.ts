import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Observable, Subject, of, throwError } from 'rxjs';
import { TicketsApiService } from '../../core/api/tickets-api.service';
import { ApiError } from '../../core/http/api-error';
import { BulkUpdateResponse, PagedResult, Ticket, TicketQuery, TicketSummary } from '../../core/models/ticket.models';
import { SEARCH_DEBOUNCE_MS, TicketsStore } from './tickets.store';

function ticket(id: number, overrides: Partial<Ticket> = {}): Ticket {
  return {
    id,
    title: `Ticket ${id}`,
    organizationName: 'Acme',
    status: 'New',
    priority: 'Medium',
    assignedTo: null,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    rowVersion: `rv-${id}-a`,
    ...overrides,
  };
}

function page(items: Ticket[], pageNumber = 1): PagedResult<Ticket> {
  return { items, page: pageNumber, pageSize: 20, totalCount: items.length, totalPages: 1 };
}

const summary: TicketSummary = { totalCount: 0, byStatus: [], byPriority: [], topAssigneesByOpenTickets: [], generatedAt: '' };

describe('TicketsStore', () => {
  /** One Subject per getTickets call, so each test controls when (and whether) a response arrives. */
  let listRequests: { query: TicketQuery; response: Subject<PagedResult<Ticket>> }[];
  let api: jasmine.SpyObj<TicketsApiService>;
  let store: TicketsStore;

  beforeEach(() => {
    listRequests = [];
    api = jasmine.createSpyObj<TicketsApiService>('TicketsApiService', [
      'getTickets', 'getSummary', 'getTicket', 'getHistory', 'updateStatus', 'bulkUpdateStatus',
    ]);
    api.getTickets.and.callFake((query: TicketQuery): Observable<PagedResult<Ticket>> => {
      const response = new Subject<PagedResult<Ticket>>();
      listRequests.push({ query, response });
      return response;
    });
    api.getSummary.and.returnValue(of(summary));

    TestBed.configureTestingModule({
      providers: [TicketsStore, { provide: TicketsApiService, useValue: api }],
    });
    store = TestBed.inject(TicketsStore);
  });

  const lastQuery = () => listRequests[listRequests.length - 1].query;

  function loadList(items: Ticket[]): void {
    listRequests[listRequests.length - 1].response.next(page(items));
  }

  describe('search', () => {
    it('sends a single request once typing pauses for the debounce time', fakeAsync(() => {
      expect(listRequests.length).toBe(1); // initial load

      store.search('I');
      tick(100);
      store.search('In');
      tick(100);
      store.search('Inv');
      tick(SEARCH_DEBOUNCE_MS - 1);
      expect(listRequests.length).toBe(1);

      tick(1);
      expect(listRequests.length).toBe(2);
      expect(lastQuery().search).toBe('Inv');
      expect(lastQuery().page).toBe(1);
    }));

    it('ignores input that does not change the effective search (e.g. only spaces)', fakeAsync(() => {
      store.search('   ');
      tick(SEARCH_DEBOUNCE_MS);

      expect(listRequests.length).toBe(1);
    }));

    it('searches again for the same text after the filters were cleared', fakeAsync(() => {
      store.search('abc');
      tick(SEARCH_DEBOUNCE_MS);
      store.clearFilters();
      store.search('abc');
      tick(SEARCH_DEBOUNCE_MS);

      expect(listRequests.map((r) => r.query.search)).toEqual(['', 'abc', '', 'abc']);
    }));

    it('resets to the first page when the search changes', fakeAsync(() => {
      store.goToPage(3);
      store.search('x');
      tick(SEARCH_DEBOUNCE_MS);

      expect(lastQuery().page).toBe(1);
    }));
  });

  describe('switchMap', () => {
    it('cancels the previous request when the query changes, so a late response is ignored', () => {
      const first = listRequests[0];
      store.goToPage(2);
      const second = listRequests[1];

      expect(first.response.observed).withContext('first HTTP request must be unsubscribed (cancelled)').toBeFalse();

      second.response.next(page([ticket(2)], 2));
      first.response.next(page([ticket(1)], 1)); // arrives late – must not overwrite the newer result

      expect(store.list().data?.items.map((t) => t.id)).toEqual([2]);
      expect(store.list().status).toBe('success');
    });

    it('keeps the previous data visible while the next page is loading', () => {
      loadList([ticket(1)]);
      store.goToPage(2);

      expect(store.list().status).toBe('loading');
      expect(store.list().data?.items.map((t) => t.id)).toEqual([1]);
    });
  });

  describe('error state', () => {
    it('exposes the error and retries the same query', () => {
      listRequests[0].response.error(new ApiError(500, 'Server error', null, {}, null));
      expect(store.list().status).toBe('error');
      expect(store.list().error?.status).toBe(500);

      store.retry();

      expect(listRequests.length).toBe(2);
      expect(listRequests[1].query).toEqual(listRequests[0].query);
      expect(store.list().status).toBe('loading');
    });
  });

  describe('status update', () => {
    it('sends the last seen RowVersion and replaces the row with the server version on success', () => {
      const original = ticket(1);
      loadList([original]);
      const updated = ticket(1, { status: 'InProgress', rowVersion: 'rv-1-b' });
      api.updateStatus.and.returnValue(of(updated));

      store.updateStatus(original, 'InProgress');

      expect(api.updateStatus).toHaveBeenCalledWith(1, 'InProgress', 'rv-1-a');
      expect(store.list().data?.items[0]).toEqual(updated);
      expect(store.rowMessages().get(1)?.kind).toBe('success');
      expect(api.getSummary).toHaveBeenCalledTimes(1);
      expect(store.busyRows().has(1)).toBeFalse();
    });

    it('on 409 shows the current version from the response and warns the user, without reloading the summary', () => {
      const stale = ticket(1);
      loadList([stale, ticket(2)]);
      const current = ticket(1, { status: 'Waiting', rowVersion: 'rv-1-z', assignedTo: 'dana' });
      api.updateStatus.and.returnValue(throwError(() =>
        new ApiError(409, 'Concurrency conflict.', 'The ticket was modified by another user.', {}, { currentTicket: current })));

      store.updateStatus(stale, 'InProgress');

      expect(store.list().data?.items[0]).withContext('row refreshed with the fresh status and RowVersion').toEqual(current);
      expect(store.list().data?.items[1]).toEqual(ticket(2));
      expect(store.rowMessages().get(1)?.kind).toBe('warning');
      expect(store.rowMessages().get(1)?.text).toContain('משתמש אחר');
      expect(api.getSummary).not.toHaveBeenCalled();
      expect(store.busyRows().has(1)).toBeFalse();
    });

    it('on 409 without the current ticket in the response, reloads the row from the server', () => {
      loadList([ticket(1)]);
      const fresh = ticket(1, { status: 'Completed', rowVersion: 'rv-1-q' });
      api.updateStatus.and.returnValue(throwError(() => new ApiError(409, 'Conflict', null, {}, {})));
      api.getTicket.and.returnValue(of(fresh));

      store.updateStatus(ticket(1), 'InProgress');

      expect(api.getTicket).toHaveBeenCalledWith(1);
      expect(store.list().data?.items[0]).toEqual(fresh);
    });

    it('shows the server message for a rejected transition (422)', () => {
      loadList([ticket(1)]);
      api.updateStatus.and.returnValue(throwError(() =>
        new ApiError(422, 'Invalid status transition.', 'Cannot change status from New to Completed.', {}, {})));

      store.updateStatus(ticket(1), 'Completed');

      expect(store.rowMessages().get(1)).toEqual({ kind: 'error', text: 'Cannot change status from New to Completed.' });
      expect(store.list().data?.items[0]).toEqual(ticket(1));
    });
  });

  describe('bulk update', () => {
    it('keeps failed items selected with their fresh RowVersion and removes the successful ones', () => {
      const a = ticket(1);
      const b = ticket(2);
      loadList([a, b]);
      store.toggleSelection(a);
      store.toggleSelection(b);

      const response: BulkUpdateResponse = {
        requested: 2, succeeded: 1, failed: 1,
        results: [
          { id: 1, outcome: 'Updated', ticket: ticket(1, { status: 'InProgress', rowVersion: 'rv-1-b' }), message: null },
          { id: 2, outcome: 'Conflict', ticket: ticket(2, { status: 'Waiting', rowVersion: 'rv-2-b' }), message: 'modified' },
        ],
      };
      api.bulkUpdateStatus.and.returnValue(of(response));

      store.bulkUpdate('InProgress');

      expect(api.bulkUpdateStatus).toHaveBeenCalledWith('InProgress', [
        { id: 1, rowVersion: 'rv-1-a' },
        { id: 2, rowVersion: 'rv-2-a' },
      ]);
      expect([...store.selection().keys()]).toEqual([2]);
      expect(store.selection().get(2)?.rowVersion).toBe('rv-2-b');
      expect(store.list().data?.items.map((t) => t.status)).toEqual(['InProgress', 'Waiting']);
      expect(store.bulkResult()).toEqual(response);
      expect(api.getSummary).toHaveBeenCalledTimes(1);
    });

    it('does not select more than 100 tickets', () => {
      const tickets = Array.from({ length: 101 }, (_, i) => ticket(i + 1));
      tickets.forEach((t) => store.toggleSelection(t));

      expect(store.selection().size).toBe(100);
      expect(store.selectionFull()).toBeTrue();
    });
  });
});
