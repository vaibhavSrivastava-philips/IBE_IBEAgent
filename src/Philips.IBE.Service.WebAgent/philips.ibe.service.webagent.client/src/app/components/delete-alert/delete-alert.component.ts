import { Component, EventEmitter, Input, Output } from '@angular/core';
import {
  ButtonComponent,
  DialogComponent,
  DialogTitleComponent,
  DialogContentComponent,
  DialogActionsComponent
} from '@filament/angular';

@Component({
  selector: 'app-delete-alert',
  templateUrl: './delete-alert.component.html',
  styleUrl: './delete-alert.component.scss',
  standalone: true,
  imports: [
    ButtonComponent,
    DialogComponent,
    DialogTitleComponent,
    DialogContentComponent,
    DialogActionsComponent
  ]
})
export class DeleteAlertComponent {

  @Output() close = new EventEmitter<boolean>();
  @Input() commpointName = "";

  onDelete() {
    this.close.emit(true)
  }
  dismissForAlert() {
    this.close.emit(false)
  }
  
}
