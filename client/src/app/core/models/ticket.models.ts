/** Types mirroring the API contract (enums are serialized by name). */

export type TicketStatus = 'New' | 'InProgress' | 'Waiting' | 'Completed';
export type TicketPriority = 'Low' | 'Medium' | 'High';

export const TICKET_STATUSES: readonly TicketStatus[] = ['New', 'InProgress', 'Waiting', 'Completed'];
export const TICKET_PRIORITIES: readonly TicketPriority[] = ['Low', 'Medium', 'High'];

export interface Ticket {
  id: number;
  title: string;
  organizationName: string;
  status: TicketStatus;
  priority: TicketPriority;
  assignedTo: string | null;
  createdAt: string;
  updatedAt: string;
  /** Base64 RowVersion – must be sent back on status updates (optimistic concurrency). */
  rowVersion: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export type SortField = 'createdAt' | 'updatedAt' | 'priority' | 'status' | 'title' | 'organizationName';
export type SortDirection = 'asc' | 'desc';

/** Everything the list screen sends to GET /api/tickets. Dates are 'YYYY-MM-DD' (UTC days). */
export interface TicketQuery {
  page: number;
  pageSize: number;
  status: TicketStatus[];
  priority: TicketPriority[];
  organizationName: string;
  assignedTo: string;
  createdFrom: string;
  createdTo: string;
  search: string;
  sortBy: SortField;
  sortDir: SortDirection;
}

export interface TicketHistoryItem {
  id: number;
  previousStatus: TicketStatus;
  newStatus: TicketStatus;
  changedAt: string;
  changedBy: string;
  correlationId: string | null;
}

export interface TicketSummary {
  totalCount: number;
  byStatus: { status: TicketStatus; count: number }[];
  byPriority: { priority: TicketPriority; count: number }[];
  topAssigneesByOpenTickets: { assignedTo: string; openCount: number }[];
  generatedAt: string;
}

export type StatusUpdateOutcome = 'Updated' | 'NotFound' | 'InvalidTransition' | 'Conflict';

export interface BulkUpdateItemResult {
  id: number;
  outcome: StatusUpdateOutcome;
  /** Updated ticket, or its current state for Conflict / InvalidTransition; null for NotFound. */
  ticket: Ticket | null;
  message: string | null;
}

export interface BulkUpdateResponse {
  requested: number;
  succeeded: number;
  failed: number;
  results: BulkUpdateItemResult[];
}
