import { Injectable, TemplateRef } from '@angular/core';
import { Subject } from 'rxjs';

export interface Message {
  severity?: 'info' | 'warn' | 'error' | 'success';
  title?: string;
  body?: string;
  id?: any;
  icon?: string;
  key?: string;
  life?: number;
  type?: 'SINGLE_LINE' | null;
  avatar?: string;
  template?: TemplateRef<any>;
  sticky?: boolean;
  closable?: boolean;
  buttons?: {
    label: string;
    action: () => void;
  }[];
  classNames?: string;
  data?: any;
}

export interface IMessageService {
  add(message: Message): void;
  addAll(messages: Message[]): void;
  clearAll(key?: string): void;
  clearMessage(message: Message): void;
}

@Injectable({
  providedIn: 'root'
})
export class MessageService implements IMessageService {

  private readonly messageSource = new Subject<Message | Message[]>();
  private readonly clearSource = new Subject<string>();
  private readonly clearMessageSource = new Subject<Message>();

  messageObserver = this.messageSource.asObservable();
  clearObserver = this.clearSource.asObservable();
  clearMessageObserver = this.clearMessageSource.asObservable();

  add(message: Message): void {
    this.messageSource.next(message);
  }

  addAll(messages: Message[]): void {
    this.messageSource.next(messages);
  }

  clearAll(key?: string): void {
    this.clearSource.next(key ?? '');
  }

  clearMessage(message: Message): void {
    this.clearMessageSource.next(message);
  }
}
