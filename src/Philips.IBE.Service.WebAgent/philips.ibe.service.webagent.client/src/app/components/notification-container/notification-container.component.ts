import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { Message, MessageService } from '../../services/message.service';

@Component({
  selector: 'app-notification-container',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="notification-container">
      @for (msg of messages; track msg.id) {
        <div class="notification-toast notification-{{ msg.severity || 'info' }}">
          <div class="notification-accent-bar"></div>
          <div class="notification-content">
            <div class="notification-header">
              <div class="notification-title-row">
                <span class="notification-icon">
                  @switch (msg.severity) {
                    @case ('success') { &#10003; }
                    @case ('error')   { &#9888; }
                    @case ('warn')    { &#9888; }
                    @default          { &#8505; }
                  }
                </span>
                <span class="notification-title">{{ msg.title }}</span>
              </div>
              <button class="notification-close" (click)="dismiss(msg)" aria-label="Dismiss notification">&#x2715;</button>
            </div>
            @if (msg.body) {
              <div class="notification-body">{{ msg.body }}</div>
            }
            @if (msg.buttons && msg.buttons.length > 0) {
              <div class="notification-actions">
                @for (btn of msg.buttons; track btn.label) {
                  <button class="notification-btn" (click)="btn.action()">{{ btn.label }}</button>
                }
              </div>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    .notification-container {
      position: fixed;
      top: 24px;
      right: 24px;
      z-index: 9999;
      display: flex;
      flex-direction: column;
      gap: 12px;
      width: 360px;
      max-width: calc(100vw - 48px);
    }

    .notification-toast {
      display: flex;
      flex-direction: row;
      background: #ffffff;
      border-radius: 8px;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.12), 0 1px 4px rgba(0, 0, 0, 0.08);
      overflow: hidden;
      animation: slideIn 0.25s ease;
      word-break: break-word;
      border: 1px solid #e5e7eb;
    }

    @keyframes slideIn {
      from { opacity: 0; transform: translateX(40px); }
      to   { opacity: 1; transform: translateX(0); }
    }

    /* Accent bar */
    .notification-accent-bar {
      width: 5px;
      flex-shrink: 0;
      border-radius: 8px 0 0 8px;
    }

    .notification-info    .notification-accent-bar { background-color: #0073e6; }
    .notification-success .notification-accent-bar { background-color: #2e7d32; }
    .notification-warn    .notification-accent-bar { background-color: #e65100; }
    .notification-error   .notification-accent-bar { background-color: #c62828; }

    /* Severity text & icon colors */
    .notification-info    .notification-title,
    .notification-info    .notification-icon { color: #0073e6; }

    .notification-success .notification-title,
    .notification-success .notification-icon { color: #2e7d32; }

    .notification-warn    .notification-title,
    .notification-warn    .notification-icon { color: #e65100; }

    .notification-error   .notification-title,
    .notification-error   .notification-icon { color: #c62828; }

    /* Button severity colors */
    .notification-info    .notification-btn { color: #0073e6; border-color: #0073e6; }
    .notification-info    .notification-btn:hover { background-color: #e8f1fd; }

    .notification-success .notification-btn { color: #2e7d32; border-color: #2e7d32; }
    .notification-success .notification-btn:hover { background-color: #e8f5e9; }

    .notification-warn    .notification-btn { color: #e65100; border-color: #e65100; }
    .notification-warn    .notification-btn:hover { background-color: #fff3e0; }

    .notification-error   .notification-btn { color: #c62828; border-color: #c62828; }
    .notification-error   .notification-btn:hover { background-color: #fdecea; }

    /* Inner content */
    .notification-content {
      flex: 1;
      padding: 20px 16px 20px 18px;
      min-width: 0;
    }

    .notification-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 8px;
    }

    .notification-title-row {
      display: flex;
      align-items: center;
      gap: 8px;
      flex: 1;
      min-width: 0;
    }

    .notification-icon {
      font-size: 17px;
      font-weight: 700;
      flex-shrink: 0;
      line-height: 1;
    }

    .notification-title {
      font-weight: 600;
      font-size: 15px;
      line-height: 1.4;
      color: #111827;
    }

    .notification-close {
      background: none;
      border: none;
      color: #9ca3af;
      cursor: pointer;
      font-size: 14px;
      width: 26px;
      height: 26px;
      display: flex;
      align-items: center;
      justify-content: center;
      border-radius: 4px;
      flex-shrink: 0;
      transition: color 0.15s, background 0.15s;
    }
    .notification-close:hover {
      color: #374151;
      background: #f3f4f6;
    }
    .notification-close:focus-visible {
      outline: 2px solid #6b7280;
      outline-offset: 2px;
    }

    .notification-body {
      margin-top: 8px;
      font-size: 14px;
      color: #4b5563;
      line-height: 1.5;
      padding-left: 26px;
    }

    .notification-actions {
      margin-top: 12px;
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      padding-left: 24px;
    }

    .notification-btn {
      background: transparent;
      border: 1px solid currentColor;
      border-radius: 5px;
      padding: 6px 16px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: background 0.15s;
      line-height: 1.4;
    }
    .notification-btn:focus-visible {
      outline: 2px solid currentColor;
      outline-offset: 2px;
    }
  `]
})
export class NotificationContainerComponent implements OnInit, OnDestroy {
  messages: (Message & { id: number })[] = [];
  private counter = 0;
  private readonly subs = new Subscription();

  constructor(private readonly messageService: MessageService) {}

  ngOnInit(): void {
    this.subs.add(
      this.messageService.messageObserver.subscribe(msg => {
        if (Array.isArray(msg)) {
          msg.forEach(m => this.addMessage(m));
        } else {
          this.addMessage(msg);
        }
      })
    );

    this.subs.add(
      this.messageService.clearMessageObserver.subscribe(msg => {
        this.messages = this.messages.filter(m => m !== msg);
      })
    );

    this.subs.add(
      this.messageService.clearObserver.subscribe(key => {
        if (key) {
          this.messages = this.messages.filter(m => m.key !== key);
        } else {
          this.messages = [];
        }
      })
    );
  }

  private addMessage(msg: Message): void {
    const id = ++this.counter;
    const enriched = { ...msg, id };
    this.messages.push(enriched);

    const life = msg.life ?? (msg.sticky ? 0 : 5000);
    if (life > 0) {
      setTimeout(() => this.dismiss(enriched), life);
    }
  }

  dismiss(msg: Message & { id: number }): void {
    this.messages = this.messages.filter(m => m.id !== msg.id);
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }
}
