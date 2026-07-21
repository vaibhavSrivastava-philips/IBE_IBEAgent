import { Component, EventEmitter, Input, Output } from '@angular/core';
import {
  ButtonComponent,
  DialogComponent,
  DialogTitleComponent,
  DialogContentComponent,
  DialogActionsComponent
} from '@filament/angular';

@Component({
  selector: 'app-info-dialog',
  templateUrl: './info-dialog.component.html',
  styleUrl: './info-dialog.component.scss',
  standalone: true,
  imports: [
    ButtonComponent,
    DialogComponent,
    DialogTitleComponent,
    DialogContentComponent,
    DialogActionsComponent
  ]
})
export class InfoDialogComponent {

  @Output() close = new EventEmitter<boolean>();
  @Input() title = "";
  @Input() message = "";
  @Input() buttonName = "";

  onSelect() {
    this.close.emit(true)
  }
  dismissForAlert() {
    this.close.emit(false)
  }
  
}
