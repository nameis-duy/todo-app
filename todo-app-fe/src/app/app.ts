import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AppearanceService } from './shared/appearance';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  template: '<router-outlet />',
})
export class App {
  constructor() { inject(AppearanceService); }
}
