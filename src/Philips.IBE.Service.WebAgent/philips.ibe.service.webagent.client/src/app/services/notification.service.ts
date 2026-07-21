import { inject, Injectable } from '@angular/core';
import { ToastService } from '@filament/angular';
import { v4 as uuidv4 } from 'uuid';

type FilamentSignal = 'default' | 'warning' | 'caution' | 'error' | 'success';

@Injectable({
  providedIn: 'root'
})
export class NotificationService {

  private readonly toastService = inject(ToastService);

  showMessage(severity: any, title: any, body: any) {
    const signalMap: Record<string, FilamentSignal> = {
      success: 'success',
      error: 'error',
      warning: 'warning',
      warn: 'warning',
      info: 'default'
    };
    const signal: FilamentSignal = signalMap[severity] ?? 'default';
    this.toastService.open({
      id: uuidv4(),
      title,
      content: body,
      signal,
      showCloseButton: true,
      timeout: 3000
    });
  }

}
