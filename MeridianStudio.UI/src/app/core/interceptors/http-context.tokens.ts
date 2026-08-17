import { HttpContextToken } from '@angular/common/http';

/**
 * Set this token on a request to suppress error logging in the global
 * error interceptor. Use for best-effort / background calls where a
 * failure is expected and handled locally (e.g. provider status polling).
 *
 * Usage:
 *   http.get(url, { context: new HttpContext().set(SILENT_ERROR, true) })
 */
export const SILENT_ERROR = new HttpContextToken<boolean>(() => false);
