import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastsRendererComponent } from '@filament/angular';
import '@filament/fonts';
import { NotificationContainerComponent } from './components/notification-container/notification-container.component';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
  standalone: true,
  imports: [
    RouterOutlet,
    ToastsRendererComponent,
    NotificationContainerComponent
  ]
})
export class AppComponent {
  title = 'angularapp1.client';
}
