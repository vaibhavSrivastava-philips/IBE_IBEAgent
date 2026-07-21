import { Component, HostListener, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ButtonComponent,
  TextComponent,
  ExpanderComponent,
  ExpanderHeaderComponent,
  ExpanderContentComponent
} from '@filament/angular';
import { 
  EditIconComponent, 
  TrashcanIconComponent 
} from '@filament-icons/angular';
import { ContractDialogComponent } from '../../components/contract-dialog/contract-dialog.component';
import { InfoDialogComponent } from '../../components/info-dialog/info-dialog.component';
import { Contract } from '../../models/Contract';
import { CommunicationData } from '../../models/CommunicationData';
import { ContractsService } from '../../services/contracts.service';
import { CommunicationDataService } from '../../services/communication-data.service';
import { NotificationService } from '../../services/notification.service';


@Component({
  selector: 'app-contract',
  templateUrl: './contract.component.html',
  styleUrl: './contract.component.scss',
  standalone: true,
  imports: [
    CommonModule,
    ButtonComponent,
    TextComponent,
    ExpanderComponent,
    ExpanderHeaderComponent,
    ExpanderContentComponent,
    EditIconComponent,
    TrashcanIconComponent,
    ContractDialogComponent,
    InfoDialogComponent
  ]
})
export class ContractComponent {
  deleteSuccess: boolean = false;
  errorMessage: string = '';
  isAlertOpen: boolean = false;
  communicationData: CommunicationData[] = [];
  contractData!: Contract[];
  isContractDialogOpen: boolean = false;
  isEditMode: boolean = false;
  contractNameSet: Set<string> = new Set<string>();
  isDialogOpen: boolean = false;
  isCertificationEnabled: boolean = false;
  dialogTitle: string = '';
  dialogMessage: string = '';
  toDelete: Contract = {
    acknowledgement: {
      isEnabled: false,
      isEnhanced: false
    },
    highFidelity: {
      isEnabled: false,
      batchCount: '1',
      timeLimit: '1'
    },
    inputIDs: [],
    name: '',
    outputID: -1
  }

  editData: Contract = {
    acknowledgement: {
      isEnabled: false,
      isEnhanced: false
    },
    highFidelity: {
      isEnabled: false,
      batchCount: '1',
      timeLimit: '1'
    },
    inputIDs: [],
    name: '',
    outputID: -1
  };

  newContractPoint: Contract = {
    acknowledgement: {
      isEnabled: false,
      isEnhanced: false
    },
    highFidelity: {
      isEnabled: false,
      batchCount: '1',
      timeLimit: '1'
    },
    inputIDs: [],
    name: '',
    outputID: -1
  };

  constructor(
    private contractsServie: ContractsService,
    private communicationDataService: CommunicationDataService,
    private readonly notificationService: NotificationService,
    private readonly cdr: ChangeDetectorRef
  ) { }


  ngOnInit() {
    this.getAllCommunicationData();
    this.resetNewContractPoint();
  }

  getAllCommunicationData(): void {
    this.communicationDataService.getAllCommunicationData().subscribe({
      next: (data) => {
        this.communicationData = data;
        this.cdr.detectChanges();
        //this.notificationService.showMessage('success','Success','Communication Points Fetched Succesfully');
        this.getAllContract();
      },
      error: (error) => {
        console.error('Error fetching communication data:', error);
        this.errorMessage = 'Failed to load communication data. Please try again.';
        this.notificationService.showMessage('error', 'Failure', this.errorMessage);
      }
    });
  }


  fetchCommunicationData(id: number) {
    if (id === undefined || id === -1) {
      return "";
    }
    return this.communicationData.filter(x => x.id === id)[0].name;
  }

  onGetAllContract() {

    this.getAllContract();
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
        this.cdr.detectChanges();
        // this.notificationService.showMessage('success','Success','Contracts Fetched Succesfully');
      },
      error: (error) => {
        this.notificationService.showMessage('error', 'Failure', 'Failed to load contract data.' + error.message);
      }
    });
  }

  openContractDialog() {
    this.isEditMode = false;
    this.resetNewContractPoint();
    this.isContractDialogOpen = true;
  }

  closeContractDialog(isOpen: any) {
    this.isContractDialogOpen = isOpen;
  }

  resetNewContractPoint() {
    this.newContractPoint = {
      acknowledgement: {
        isEnabled: false,
        isEnhanced: false
      },
      highFidelity: {
        isEnabled: false,
        batchCount: '1',
        timeLimit: '1'
      },
      inputIDs: [],
      name: '',
      outputID: -1
    };
  }

  edit(item: Contract) {
    this.isEditMode = true;
    this.editData = item;
    this.isContractDialogOpen = true;
  }

  deleteAlertComponent(toDelete: Contract) {
    this.dialogMessage = `Are you sure you want to delete contract : ${toDelete.name}?`;
    this.dialogTitle = 'Delete Contract';
    this.isAlertOpen = true;
    this.toDelete = toDelete;
  }

  delete() {
    this.contractsServie.deleteContract(this.toDelete.name).subscribe({
      next: () => {
        this.deleteSuccess = true;
        this.errorMessage = '';
        console.log(`Contract point with ID ${this.toDelete.name} deleted successfully.`);
        this.onGetAllContract()
      },
      error: (error) => {
        this.deleteSuccess = false;
        this.errorMessage = `Error deleting contract point: ${error.message}`;
        console.error('Error deleting contract point:', error);
      },
    });
  }

  closeAlert(isDeleted: any) {
    if (isDeleted) {
      this.delete();
    }
    this.isAlertOpen = false;
  }


  @HostListener('document:keydown.escape')
  handleEscapeKey() {
    if (this.isDialogOpen) {
      this.closeContractDialog(false);
    }
  }

  onOverlayClick(event: MouseEvent) {
    if ((event.target as HTMLElement).classList.contains('dialog-overlay')) {
      this.closeContractDialog(false);
    }
  }
}
