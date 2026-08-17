import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { GlobalLoadingService } from './core/services/global-loading.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: `
    @if (loading.isLoading()) {
      <div
        role="progressbar"
        aria-label="Loading"
        class="fixed top-0 left-0 right-0 z-50 h-0.5 bg-indigo-500 animate-pulse"
      ></div>
    }
    <router-outlet />
  `,
})
export class AppComponent {
  protected readonly loading = inject(GlobalLoadingService);
}
