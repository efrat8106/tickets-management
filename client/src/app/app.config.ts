import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { apiErrorInterceptor } from './core/http/api-error.interceptor';
import { userNameInterceptor } from './core/http/user-name.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideHttpClient(withInterceptors([userNameInterceptor, apiErrorInterceptor])),
  ],
};
