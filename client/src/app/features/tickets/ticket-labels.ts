import { SortField, TicketPriority, TicketStatus } from '../../core/models/ticket.models';

export const STATUS_LABELS: Record<TicketStatus, string> = {
  New: 'חדשה',
  InProgress: 'בטיפול',
  Waiting: 'ממתינה',
  Completed: 'הושלמה',
};

export const PRIORITY_LABELS: Record<TicketPriority, string> = {
  Low: 'נמוכה',
  Medium: 'בינונית',
  High: 'גבוהה',
};

export const SORT_LABELS: Record<SortField, string> = {
  title: 'כותרת',
  organizationName: 'ארגון',
  status: 'סטטוס',
  priority: 'עדיפות',
  createdAt: 'נוצרה',
  updatedAt: 'עודכנה',
};

/**
 * Allowed transitions, used only to offer sensible choices in the UI. The server is the source of truth and
 * rejects anything else with 422, so a mismatch here can never corrupt data.
 */
export const ALLOWED_TRANSITIONS: Record<TicketStatus, readonly TicketStatus[]> = {
  New: ['InProgress'],
  InProgress: ['Waiting', 'Completed'],
  Waiting: ['InProgress', 'Completed'],
  Completed: ['InProgress'],
};
