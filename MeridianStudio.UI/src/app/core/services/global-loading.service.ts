import { Injectable, computed, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class GlobalLoadingService {
  private readonly inFlightCount = signal(0);

  readonly isLoading = computed(() => this.inFlightCount() > 0);

  increment(): void {
    this.inFlightCount.update(n => n + 1);
  }

  decrement(): void {
    this.inFlightCount.update(n => Math.max(0, n - 1));
  }
}
