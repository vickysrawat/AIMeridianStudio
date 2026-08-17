import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs/operators';
import { GlobalLoadingService } from '../services/global-loading.service';

export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  const loadingService = inject(GlobalLoadingService);
  loadingService.increment();
  return next(req).pipe(finalize(() => loadingService.decrement()));
};
