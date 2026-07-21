import { CommunicationData } from './../../models/CommunicationData';
import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ButtonComponent,
  TextComponent,
  DataGridComponent
} from '@filament/angular';
import {
  SortingDefaultIconComponent,
  SortingDownIconComponent,
  SortingUpIconComponent
} from '@filament-icons/angular';
import { ErrorQueue } from '../../models/error-queue';
import { ErrorQueueService } from '../../services/error-queue.service';
import { interval, Subscription } from 'rxjs';
import { CommunicationDataService } from '../../services/communication-data.service';
import { NotificationService } from '../../services/notification.service';
import { InfoDialogComponent } from '../../components/info-dialog/info-dialog.component';
import {
  ColumnDef,
  createAngularTable,
  getCoreRowModel,
  getSortedRowModel,
  FlexRenderDirective
} from '@tanstack/angular-table';


@Component({
  selector: 'app-error-queue',
  templateUrl: './error-queue.component.html',
  styleUrls: ['./error-queue.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    ButtonComponent,
    TextComponent,
    DataGridComponent,
    SortingDefaultIconComponent,
    SortingDownIconComponent,
    SortingUpIconComponent,
    FlexRenderDirective,
    InfoDialogComponent
  ]
})
export class ErrorQueueComponent implements OnInit, OnDestroy {
  errorQueue = signal<ErrorQueue[]>([]);
  intervalSubscription: Subscription;
  communicationData: CommunicationData[] = [];
  visible: boolean = false;
  dialogMessage: string = '';
  dialogTitle: string = '';

  columns: ColumnDef<ErrorQueue>[] = [
    { accessorKey: 'id', header: 'ID' },
    { accessorKey: 'senderId', header: 'Sender' },
    { accessorKey: 'timeStamp', header: 'Timestamp' },
    {
      id: 'message',
      header: 'Message',
      enableSorting: false,
    },
  ];

  table = createAngularTable(() => ({
    data: this.errorQueue(),
    columns: this.columns,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  }));
  constructor(private errorQueueService: ErrorQueueService,
    private communicationDataService: CommunicationDataService,
    private notificationService: NotificationService
  ) {
    this.intervalSubscription = new Subscription();
  }

  ngOnDestroy(): void {
    if (this.intervalSubscription) {
      this.intervalSubscription.unsubscribe();
    }
  }

  ngOnInit(): void {
    this.getAllCommunicationData();
    this.fetchErrorQueue();
    this.intervalSubscription = interval(5000).subscribe(() => {
      this.fetchErrorQueue();
    });
  }

  showErrorDetails(item:ErrorQueue){
    this.visible = true;
    this.dialogTitle = 'Message Details';
    this.dialogMessage = item.message;
  }


  fetchErrorQueue(): void {
    this.errorQueueService.getErrorQueue().subscribe({
      next: (data) => {
        this.errorQueue.set(data);
      },
      error: (error) => {
       this.notificationService.showMessage('error', 'Error Fetching Error Queue', error);
      }
    });
  }

  updateErrorQueue(item: ErrorQueue): void {
    this.errorQueueService.UpdateErrorQueue(item.id).subscribe({
      next: (data) => {
        this.notificationService.showMessage('success', 'Error Queue Updated', '');
        this.fetchErrorQueue();
      },
      error: (error) => {
        this.notificationService.showMessage('error', 'Error Queue Update Failed', error);
      }
    });
  }

  getAllCommunicationData(): void {
    this.communicationDataService.getAllCommunicationData().subscribe({
      next: (data) => {
        this.communicationData = data;

      },
      error: (error) => {
        this.notificationService.showMessage('error', 'Error Fetching Communication Data', error);
      }
    });
  }

  getCommunicationDataById(id: number): string {
    let name = '';
    let commpoint = this.communicationData.find(data => data.id === id);
    if(commpoint!== undefined){
      name = commpoint.name;
    }
    return name;
  }
}

