import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'workspace',
    pathMatch: 'full',
  },
  {
    path: 'workspace',
    loadComponent: () =>
      import('./features/workspace/workspace.component').then(m => m.WorkspaceComponent),
  },
  {
    path: '**',
    redirectTo: 'workspace',
  },
];
