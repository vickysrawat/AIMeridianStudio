import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { ApiError } from '../models/interfaces';
import { SILENT_ERROR } from './http-context.tokens';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      const normalized: ApiError = normalizeHttpError(err);
      if (!req.context.get(SILENT_ERROR)) {
        console.error(`[HTTP ${normalized.statusCode}] ${normalized.message}`, normalized);
      }
      return throwError(() => normalized);
    }),
  );
};

function normalizeHttpError(err: HttpErrorResponse): ApiError {
  if (err.error && typeof err.error === 'object') {
    const body = err.error as Partial<ApiError>;
    return {
      statusCode: err.status,
      message: body.message ?? err.message ?? 'Request failed',
      errors: body.errors,
      traceId: body.traceId,
    };
  }

  const statusMessages: Record<number, string> = {
    0: 'Unable to reach the server. Check your network connection.',
    400: 'The request contained invalid data.',
    401: 'Authentication required. Please sign in.',
    403: 'You do not have permission to perform this action.',
    404: 'The requested resource was not found.',
    409: 'A conflict occurred with the current state of the resource.',
    422: 'The request data failed validation.',
    429: 'Too many requests. Please wait a moment and try again.',
    500: 'An internal server error occurred. Please try again later.',
    503: 'The service is temporarily unavailable.',
  };

  return {
    statusCode: err.status,
    message: statusMessages[err.status] ?? err.message ?? 'An unexpected error occurred',
  };
}
