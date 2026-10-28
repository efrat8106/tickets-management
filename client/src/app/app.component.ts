import { Component, inject } from '@angular/core';
import { CurrentUserService } from './core/user/current-user.service';
import { TicketListComponent } from './features/tickets/ticket-list/ticket-list.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [TicketListComponent],
  template: `
    <header class="app-header">
      <h1>ניהול פניות</h1>
      <label class="user">
        שם משתמש (לתיעוד שינויים)
        <input
          type="text"
          maxlength="100"
          placeholder="system"
          [value]="currentUser.userName()"
          (change)="currentUser.setUserName($any($event.target).value)"
        />
      </label>
    </header>
    <main class="app-main">
      <app-ticket-list />
    </main>
  `,
  styles: `
    .app-header { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 1rem;
      padding: 0.75rem 1rem; border-bottom: 1px solid var(--border); background: var(--surface); }
    h1 { margin: 0; font-size: 1.25rem; }
    .user { display: flex; align-items: center; gap: 0.5rem; font-size: 0.875rem; }
    .app-main { padding: 1rem; max-width: 1400px; margin: 0 auto; }
  `,
})
export class AppComponent {
  protected readonly currentUser = inject(CurrentUserService);
}
