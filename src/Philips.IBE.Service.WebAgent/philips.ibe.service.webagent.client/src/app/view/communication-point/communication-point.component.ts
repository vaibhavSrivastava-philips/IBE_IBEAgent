import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ButtonComponent,
  TextComponent,
  DataGridComponent
} from '@filament/angular';
import {
  SortingDefaultIconComponent,
  SortingDownIconComponent,
  SortingUpIconComponent,
  EditIconComponent,
  TrashcanIconComponent
} from '@filament-icons/angular';
import { CompointDialogComponent } from '../../components/compoint-dialog/compoint-dialog.component';
import { DeleteAlertComponent } from '../../components/delete-alert/delete-alert.component';
import { Contract } from '../../models/Contract';
import {
  ColumnDef,
  createAngularTable,
  getCoreRowModel,
  getSortedRowModel,
  FlexRenderDirective
} from '@tanstack/angular-table';
import { CommunicationData } from '../../models/CommunicationData';
import { CommunicationDataService } from '../../services/communication-data.service';
import { CertificateService } from '../../services/certificate.service';
import { catchError, of, tap } from 'rxjs';
import { NotificationService } from '../../services/notification.service';
import { ContractsService } from '../../services/contracts.service';
import { getDefaultCommunicationData } from '../../models/CommunicationComponent';


@Component({
  selector: 'app-communication-point',
  templateUrl: './communication-point.component.html',
  styleUrl: './communication-point.component.scss',
  standalone: true,
  imports: [
    CommonModule,
    ButtonComponent,
    TextComponent,
    DataGridComponent,
    SortingDefaultIconComponent,
    SortingDownIconComponent,
    SortingUpIconComponent,
    EditIconComponent,
    TrashcanIconComponent,
    FlexRenderDirective,
    CompointDialogComponent,
    DeleteAlertComponent
  ]
})
export class CommunicationPointComponent {
  id: number = 0;
  isDialogOpen: boolean = false;
  isAlertOpen: boolean = false;
  deleteSuccess: boolean = false;
  errorMessage: string = '';
  contractData!: Contract[];
  contractNameSet: Set<string> = new Set<string>();
  toDelete: CommunicationData = getDefaultCommunicationData();
  nameSet: Set<string> = new Set<string>([""]);


  constructor(
    private communicationDataService: CommunicationDataService,
    private certificateService: CertificateService,
    private notificationService: NotificationService,
    private contractsServie: ContractsService
  ) {
    this.currentCommunicationData = getDefaultCommunicationData();
  }

  communicationData = signal<CommunicationData[]>([getDefaultCommunicationData()]);

  currentCommunicationData: CommunicationData;
  isEditMode: boolean = false;

  columns: ColumnDef<CommunicationData>[] = [
    { accessorKey: 'name', header: 'Name' },
    { accessorKey: 'mode', header: 'Mode' },
    { accessorKey: 'type', header: 'Type' },
    {
      id: 'ipAddress',
      header: 'IP',
      accessorFn: (row) => row.tcpConfiguration?.ipAddress ?? '',
    },
    {
      id: 'port',
      header: 'Port',
      accessorFn: (row) => {
        const port = row.tcpConfiguration?.port;
        return port === 0 || !port ? '-' : port;
      },
    },
    {
      id: 'actions',
      header: 'Action',
      enableSorting: false,
    },
  ];

  table = createAngularTable(() => ({
    data: this.communicationData(),
    columns: this.columns,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  }));
  ngOnInit() {
    this.getAllCommunicationData();
    this.getAllContract();
  }

  onGetAllCommunicationData() {
    this.getAllCommunicationData();
  }

  getAllContract(): void {
    this.contractsServie.getAllContract().subscribe({
      next: (data) => {
        this.contractData = data;
        this.contractNameSet.clear();
        this.contractData.forEach(contract => {
          this.contractNameSet.add(contract.name)
        });
        //this.contractData.forEach(contract => this.contractNameSet.add(contract.name));

        // this.notificationService.showMessage('success','Success','Contracts Fetched Succesfully');
      },
      error: (error) => {
        this.notificationService.showMessage('error', 'Failure', 'Failed to load contract data.' + error.message);
      }
    });
  }

  getAllCommunicationData(): void {
    this.communicationDataService.getAllCommunicationData().subscribe({
      next: (data) => {
        this.communicationData.set(data);
        this.nameSet = new Set(data.map(d => d.name));
        // this.notificationService.showMessage('success', 'Success', 'Communication Points Fetched Successfully');
      },
      error: (error) => {
        this.errorMessage = 'Failed to load communication data. Please try again. Error Message :' + error;
        this.notificationService.showMessage('error', 'Failure', this.errorMessage);
      }
    });
  }


  //Button Handlers

  edit(input: CommunicationData) {
    this.isDialogOpen = true;
    this.isEditMode = true;
    this.currentCommunicationData = input;
  }

  deleteAlertComponent(toDelete: CommunicationData) {
    this.isAlertOpen = true;
    this.toDelete = toDelete;
  }

  delete() {
    this.isAlertOpen = true;

    if (this.toDelete?.id < 0) {
      this.errorMessage = 'Communication point ID is missing.';
      console.error(this.errorMessage);
      return;
    }

    const isIdInContract = this.contractData.some(contract =>
      contract.inputIDs.includes(this.toDelete.id) || contract.outputID === this.toDelete.id
    );

    if (isIdInContract) {

      this.notificationService.showMessage(
        'info',
        'Warning',
        `Communication point (${this.toDelete.name}) is referenced in a contract and cannot be deleted.`
      );
      return;
    }

    this.communicationDataService.deleteCommunicationData(this.toDelete.id).subscribe({
      next: () => {
        this.deleteSuccess = true;
        this.errorMessage = '';
        this.onGetAllCommunicationData();

        if (this.toDelete.name) {
          console.log(this.toDelete.name);
          this.deleteCertificate();
        } else {
          this.notificationService.showMessage('error', 'Failure', 'Folder name is missing. Cannot delete folder.');
        }
      },
      error: (error) => {
        this.deleteSuccess = false;
        this.errorMessage = `Error deleting communication point: ${error.message}`;
        this.notificationService.showMessage('error', 'Failure', this.errorMessage);
      }
    });
  }

  private deleteCertificate() {
    this.certificateService.deleteFolder(this.toDelete.name)
      .pipe(
        tap(response => {
          console.log('Folder deleted successfully', response);
          this.toDelete.certificateDetails.rootCertificatePath = '';
          this.toDelete.certificateDetails.clientCertificatePath = '';
        }),
        catchError(error => {
          console.error('Folder deletion failed', error);
          return of(null);
        })
      )
      .subscribe();
  }

  //Dialog Methods
  openDialog() {
    this.currentCommunicationData = getDefaultCommunicationData();
    this.isDialogOpen = true;
  }

  closeDialog(isOpen: any) {
    this.isEditMode = false;
    this.isDialogOpen = isOpen;
  }

  closeAlert(isDeleted: any) {
    if (isDeleted) {
      this.delete();
    }
    this.isAlertOpen = false;
  }
}
