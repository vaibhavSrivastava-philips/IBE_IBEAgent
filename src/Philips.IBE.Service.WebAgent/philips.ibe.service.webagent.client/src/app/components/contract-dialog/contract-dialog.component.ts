import { Component, Output, EventEmitter, Input, OnInit, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ButtonComponent,
  DialogComponent,
  DialogTitleComponent,
  DialogContentComponent,
  DialogActionsComponent,
  TagBoxComponent,
  ItemComponent,
  DropdownComponent,
  OptionComponent,
  ToggleSwitchComponent,
  InputDirective
} from '@filament/angular';
import { Contract } from '../../models/Contract';
import { CommunicationData } from '../../models/CommunicationData';

import { ContractsService } from '../../services/contracts.service';
import { CommunicationDataService } from '../../services/communication-data.service';

@Component({
  selector: 'app-contract-dialog',
  templateUrl: './contract-dialog.component.html',
  styleUrl: './contract-dialog.component.scss',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonComponent,
    DialogComponent,
    DialogTitleComponent,
    DialogContentComponent,
    DialogActionsComponent,
    TagBoxComponent,
    ItemComponent,
    DropdownComponent,
    OptionComponent,
    ToggleSwitchComponent,
    InputDirective
  ]
})

export class ContractDialogComponent implements OnInit {
  onInputChange(event: Event) {
    console.log('Input changed:', event);
  }

  onInputSelectionChange(keys: string[]) {
    this.selectedInputKeys = keys;
    this.currentContract.input = this.inputCommunicationData.filter(cd => keys.includes(cd.id.toString()));
  }

  onOutputSelectionChange(key: string | undefined) {
    this.selectedOutputKey = key || '';
    this.currentContract.output = this.outputCommunicationData.find(cd => cd.id.toString() === key);
    console.log('Output selection changed:', this.currentContract.output);
  }

  selectedInputKeys: string[] = [];
  selectedOutputKey: string = '';


  @Output() close = new EventEmitter<boolean>();
  @Output() getAllContract = new EventEmitter<void>();
  @Input() currentContract: Contract = {
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
  @Input() isEditMode: boolean = false;
  @Input() contractNameSet: Set<string> = new Set<string>([""]);
  ackEnum = ['Original', 'Enhanced'];
  successMessage: string = '';
  errorMessage: string = '';
  communicationData: CommunicationData[] = [];
  inputCommunicationData: CommunicationData[] = [];
  outputCommunicationData: CommunicationData[] = [];
  constructor(
    private contractsService: ContractsService,
    private communicationDataService: CommunicationDataService
  ) { }

  ngOnInit() {
    this.getAllCommunicationData();
    if (!this.isEditMode) {
      this.resetContract();
    }

  }

  private resetContract() {
    this.currentContract = {
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


  getAllCommunicationData(): void {
    this.communicationDataService.getAllCommunicationData().subscribe({
      next: (data) => {
        this.communicationData = data;
        this.inputCommunicationData = data.filter((item) => item.mode.toLowerCase() === 'input');
        this.outputCommunicationData = data.filter((item) => item.mode.toLowerCase() === 'output');

        let websocketData = this.outputCommunicationData.filter((item) => item.type === 'websocket');
        websocketData.forEach((item) => {
          this.inputCommunicationData.push(item);
          let index = this.outputCommunicationData.indexOf(item);
          this.outputCommunicationData.splice(index, 1);
        });

        if (this.isEditMode) {
          this.currentContract.output = this.outputCommunicationData.filter(x => x.id === this.currentContract.outputID)[0];
          this.selectedOutputKey = this.currentContract.output?.id?.toString() || '';
          this.currentContract.input = this.inputCommunicationData.filter(x => this.currentContract.inputIDs.includes(x.id));
          this.selectedInputKeys = this.currentContract.input?.map(cd => cd.id.toString()) || [];
        }
      },
      error: (error) => {
        console.error('Error fetching communication data:', error);
        this.errorMessage = 'Failed to load communication data. Please try again.';
      }
    });
  }

  add() {
    this.currentContract.output = this.outputCommunicationData.find(cd => cd.id.toString() === this.selectedOutputKey);
    this.currentContract.outputID = this.currentContract.output!.id;
    this.currentContract.input = this.inputCommunicationData.filter(cd => this.selectedInputKeys.includes(cd.id.toString()));
    this.currentContract.inputIDs = this.getSelectedInputIDs(this.currentContract);

    if (this.validateContract()) {
      if (!this.isEditMode) {
        this.addNewContractData();
      } else {
        this.updateExistingContractData();
      }
    }
  }

  private getSelectedInputIDs(contract: Contract): number[] {
    let selectedInputIDs: number[] = [];
    if (contract.input === undefined || contract.input == null) {
      return [];

    }
    contract.input!.forEach((cp) => {

      selectedInputIDs.push(cp.id);

    });
    return selectedInputIDs;
  }

  private validateContract(): boolean {
    let contractSet = new Set<string>();
    this.contractNameSet.forEach(x => contractSet.add(x));
    if (this.isEditMode) {
      contractSet.delete(this.currentContract.name);
    }

    if (!this.currentContract.name.trim()) {
      this.errorMessage = 'Contract name is required.';
      return false;
    }

    if (contractSet.has(this.currentContract.name)) {
      this.errorMessage = 'Contract names must be unique.';
      return false;
    }
    contractSet.add(this.currentContract.name);

    if (this.currentContract.inputIDs.length === 0) {
      this.errorMessage = 'Each contract must have at least one input.';
      return false;
    }

    return true;
  }

  private addNewContractData() {

    this.contractsService.addContract(this.currentContract).subscribe({
      next: () => {
        this.successMessage = 'Contract added successfully.';
        this.getAllContract.emit();
        this.closeDialog();
      },
      error: (error) => {
        console.error('Error adding contract:', error);
        this.errorMessage = 'Failed to add contract. Please try again.';
      }
    });
  }

  private updateExistingContractData() {
    this.contractsService.updateContract(this.currentContract.name, this.currentContract).subscribe({
      next: () => {
        this.successMessage = 'Contract updated successfully.';
        this.getAllContract.emit();
        this.closeDialog();
      },
      error: (error) => {
        console.error('Error updating contract:', error);
        this.errorMessage = 'Failed to update contract. Please try again.';
      }
    });

  }

  trackByFn(index: number, item: any) {
    return index;
  }




  closeDialog() {
    this.close.emit(false);
  }

  onOverlayClick(event: MouseEvent) {
    if ((event.target as HTMLElement).classList.contains('dialog-overlay')) {
      this.closeDialog();
    }
  }
}
