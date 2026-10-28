import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'tickets.userName';

/**
 * The name sent as X-User-Name (stored as ChangedBy in the audit). There is no authentication in this exercise;
 * the name is remembered per browser for convenience.
 */
@Injectable({ providedIn: 'root' })
export class CurrentUserService {
  readonly userName = signal(readStoredName());

  setUserName(name: string): void {
    const trimmed = name.trim();
    this.userName.set(trimmed);
    try {
      localStorage.setItem(STORAGE_KEY, trimmed);
    } catch {
      // Storage can be unavailable (private mode); the name still applies for this session.
    }
  }
}

function readStoredName(): string {
  try {
    return localStorage.getItem(STORAGE_KEY) ?? '';
  } catch {
    return '';
  }
}
