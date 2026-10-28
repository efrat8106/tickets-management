import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../http/api-error';
import { apiErrorInterceptor } from '../http/api-error.interceptor';
import { USER_NAME_HEADER, userNameInterceptor } from '../http/user-name.interceptor';
import { Ticket, TicketQuery } from '../models/ticket.models';
import { CurrentUserService } from '../user/current-user.service';
import { TicketsApiService } from './tickets-api.service';

const baseQuery: TicketQuery = {
  page: 2,
  pageSize: 50,
  status: [],
  priority: [],
  organizationName: '',
  assignedTo: '',
  createdFrom: '',
  createdTo: '',
  search: '',
  sortBy: 'title',
  sortDir: 'asc',
};

describe('TicketsApiService (with interceptors)', () => {
  let api: TicketsApiService;
  let http: HttpTestingController;
  let user: CurrentUserService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([userNameInterceptor, apiErrorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    api = TestBed.inject(TicketsApiService);
    http = TestBed.inject(HttpTestingController);
    user = TestBed.inject(CurrentUserService);
    user.userName.set('');
  });

  afterEach(() => http.verify());

  it('sends filtering, sorting and paging to the server and omits empty filters', () => {
    api.getTickets({
      ...baseQuery,
      status: ['New', 'Waiting'],
      priority: ['High'],
      assignedTo: '  dana  ',
      search: ' Inv ',
      createdFrom: '2026-01-01',
      createdTo: '2026-01-31',
    }).subscribe();

    const request = http.expectOne((r) => r.url === '/api/tickets');
    const params = request.request.params;
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('50');
    expect(params.get('sortBy')).toBe('title');
    expect(params.get('sortDir')).toBe('asc');
    expect(params.getAll('status')).toEqual(['New', 'Waiting']);
    expect(params.getAll('priority')).toEqual(['High']);
    expect(params.get('assignedTo')).toBe('dana');
    expect(params.get('search')).toBe('Inv');
    expect(params.get('createdFrom')).toBe('2026-01-01T00:00:00Z');
    expect(params.get('createdTo')).toBe('2026-01-31T23:59:59.999Z');
    expect(params.has('organizationName')).toBeFalse();
    request.flush({ items: [], page: 2, pageSize: 50, totalCount: 0, totalPages: 0 });
  });

  it('sends the RowVersion with a status update and the user name header when set', () => {
    user.userName.set('dana.cohen');

    api.updateStatus(7, 'InProgress', 'AAAAAAAAB9E=').subscribe();

    const request = http.expectOne('/api/tickets/7/status');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ newStatus: 'InProgress', rowVersion: 'AAAAAAAAB9E=' });
    expect(request.request.headers.get(USER_NAME_HEADER)).toBe('dana.cohen');
    request.flush({});
  });

  it('does not send the user name header when no name is set', () => {
    api.getSummary().subscribe();

    const request = http.expectOne('/api/tickets/summary');
    expect(request.request.headers.has(USER_NAME_HEADER)).toBeFalse();
    request.flush({});
  });

  it('turns a 409 ProblemDetails response into an ApiError carrying the current ticket', () => {
    const current = { id: 7, status: 'Waiting', rowVersion: 'AAAAAAAAB9I=' } as Ticket;
    let error: ApiError | undefined;

    api.updateStatus(7, 'InProgress', 'AAAAAAAAB9E=').subscribe({ error: (e: ApiError) => (error = e) });
    http.expectOne('/api/tickets/7/status').flush(
      { title: 'Concurrency conflict.', status: 409, detail: 'The ticket was modified by another user.', currentTicket: current },
      { status: 409, statusText: 'Conflict' },
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error?.status).toBe(409);
    expect(error?.detail).toBe('The ticket was modified by another user.');
    expect(error?.extension<Ticket>('currentTicket')).toEqual(current);
  });

  it('exposes validation errors from a 400 response as one readable message', () => {
    let error: ApiError | undefined;

    api.getTickets(baseQuery).subscribe({ error: (e: ApiError) => (error = e) });
    http.expectOne((r) => r.url === '/api/tickets').flush(
      { title: 'One or more validation errors occurred.', status: 400, errors: { pageSize: ["'pageSize' must be between 1 and 100."] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(error?.fieldErrors['pageSize']).toEqual(["'pageSize' must be between 1 and 100."]);
    expect(error?.displayMessage).toBe("'pageSize' must be between 1 and 100.");
  });
});
