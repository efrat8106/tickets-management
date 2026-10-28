import { HttpErrorResponse } from '@angular/common/http';

/**
 * Normalized API error (created by apiErrorInterceptor from ProblemDetails responses), so components and stores
 * never deal with raw HttpErrorResponse objects.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly title: string,
    readonly detail: string | null,
    readonly fieldErrors: Readonly<Record<string, string[]>>,
    private readonly body: Record<string, unknown> | null,
  ) {
    super(detail ?? title);
    this.name = 'ApiError';
  }

  /** Reads a ProblemDetails extension member, e.g. 'currentTicket' on a 409. */
  extension<T>(name: string): T | undefined {
    return this.body?.[name] as T | undefined;
  }

  static from(error: HttpErrorResponse): ApiError {
    if (error.status === 0) {
      return new ApiError(0, 'השרת אינו זמין', 'לא ניתן להתחבר לשרת. בדקו שה-API פועל ונסו שוב.', {}, null);
    }

    const body = typeof error.error === 'object' && error.error !== null ? (error.error as Record<string, unknown>) : null;
    return new ApiError(
      error.status,
      (body?.['title'] as string | undefined) ?? error.statusText ?? 'שגיאה',
      (body?.['detail'] as string | undefined) ?? null,
      (body?.['errors'] as Record<string, string[]> | undefined) ?? {},
      body,
    );
  }

  /** One readable line for the UI, including field validation messages. */
  get displayMessage(): string {
    const fields = Object.values(this.fieldErrors).flat();
    return fields.length > 0 ? fields.join(' ') : (this.detail ?? this.title);
  }
}
