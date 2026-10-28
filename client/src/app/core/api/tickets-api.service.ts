import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BulkUpdateResponse,
  PagedResult,
  Ticket,
  TicketHistoryItem,
  TicketQuery,
  TicketStatus,
  TicketSummary,
} from '../models/ticket.models';

/** HTTP access to the tickets API – no state, no UI logic. */
@Injectable({ providedIn: 'root' })
export class TicketsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/tickets';

  getTickets(query: TicketQuery): Observable<PagedResult<Ticket>> {
    return this.http.get<PagedResult<Ticket>>(this.baseUrl, { params: toHttpParams(query) });
  }

  getTicket(id: number): Observable<Ticket> {
    return this.http.get<Ticket>(`${this.baseUrl}/${id}`);
  }

  getSummary(): Observable<TicketSummary> {
    return this.http.get<TicketSummary>(`${this.baseUrl}/summary`);
  }

  getHistory(id: number, page: number, pageSize: number): Observable<PagedResult<TicketHistoryItem>> {
    return this.http.get<PagedResult<TicketHistoryItem>>(`${this.baseUrl}/${id}/history`, {
      params: { page, pageSize },
    });
  }

  updateStatus(id: number, newStatus: TicketStatus, rowVersion: string): Observable<Ticket> {
    return this.http.put<Ticket>(`${this.baseUrl}/${id}/status`, { newStatus, rowVersion });
  }

  bulkUpdateStatus(newStatus: TicketStatus, items: { id: number; rowVersion: string }[]): Observable<BulkUpdateResponse> {
    return this.http.post<BulkUpdateResponse>(`${this.baseUrl}/bulk-status`, { newStatus, items });
  }
}

/**
 * Serializes the list query. Empty values are omitted; multi-value filters are repeated (status=New&status=Waiting).
 * Date inputs are whole UTC days: createdFrom from 00:00:00, createdTo up to 23:59:59.999 (inclusive on the server).
 */
export function toHttpParams(query: TicketQuery): HttpParams {
  let params = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sortBy', query.sortBy)
    .set('sortDir', query.sortDir);

  query.status.forEach((s) => (params = params.append('status', s)));
  query.priority.forEach((p) => (params = params.append('priority', p)));

  const text: [string, string][] = [
    ['organizationName', query.organizationName.trim()],
    ['assignedTo', query.assignedTo.trim()],
    ['search', query.search.trim()],
  ];
  text.filter(([, value]) => value).forEach(([key, value]) => (params = params.set(key, value)));

  if (query.createdFrom) {
    params = params.set('createdFrom', `${query.createdFrom}T00:00:00Z`);
  }
  if (query.createdTo) {
    params = params.set('createdTo', `${query.createdTo}T23:59:59.999Z`);
  }

  return params;
}
