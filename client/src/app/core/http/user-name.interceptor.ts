import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CurrentUserService } from '../user/current-user.service';

export const USER_NAME_HEADER = 'X-User-Name';

/** Adds X-User-Name to API calls when a name is set (the server defaults to "system" otherwise). */
export const userNameInterceptor: HttpInterceptorFn = (req, next) => {
  const userName = inject(CurrentUserService).userName();
  return userName && req.url.startsWith('/api/')
    ? next(req.clone({ setHeaders: { [USER_NAME_HEADER]: userName } }))
    : next(req);
};
