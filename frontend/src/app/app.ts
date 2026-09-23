import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { HttpStatusIndicatorComponent } from './shared/ui/http-status-indicator/http-status-indicator.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HttpStatusIndicatorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
