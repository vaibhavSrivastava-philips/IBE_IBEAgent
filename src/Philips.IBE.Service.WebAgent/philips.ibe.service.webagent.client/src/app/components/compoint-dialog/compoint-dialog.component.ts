import { Component, HostListener, EventEmitter, Output, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ButtonComponent,
  DialogComponent,
  DialogTitleComponent,
  DialogContentComponent,
  DialogActionsComponent,
  DropdownComponent,
  OptionComponent,
  ToggleSwitchComponent,
  PasswordComponent,
  FlexBoxComponent,
  InputDirective
} from '@filament/angular';
import { CommunicationData } from '../../models/CommunicationData';

import { CommunicationDataService } from '../../services/communication-data.service';
import { CertificateService } from '../../services/certificate.service';
import { catchError, of, tap } from 'rxjs';
import { getDefaultCommunicationData } from '../../models/CommunicationComponent';


@Component({
  selector: 'app-compoint-dialog',
  templateUrl: './compoint-dialog.component.html',
  styleUrl: './compoint-dialog.component.scss',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonComponent,
    DialogComponent,
    DialogTitleComponent,
    DialogContentComponent,
    DialogActionsComponent,
    DropdownComponent,
    OptionComponent,
    ToggleSwitchComponent,
    PasswordComponent,
    FlexBoxComponent,
    InputDirective
  ]
})
export class CompointDialogComponent {

  constructor(private communicationDataService: CommunicationDataService, private certificateService: CertificateService) { }

  @Output() close = new EventEmitter<boolean>();

  @Output() getAllCommunicationData = new EventEmitter<void>();

  @Input() currentCommunicationData: CommunicationData = getDefaultCommunicationData();
  @Input() isEditMode: boolean = false;

  @Input() nameSet: Set<string> = new Set<string>([""]);


  successMessage: string = '';
  errorMessage: string = '';

  errorValidation: string = '';

  showCertificateDialog: boolean = false;
  isCertificateUploaded: boolean = false;

  ngOnInit() {
    if (this.currentCommunicationData.name !== '') {
      this.newCommunicationPoint = this.currentCommunicationData;

      // Normalize mode and type to lowercase so template comparisons
      // and dropdown values (which are lowercase) work correctly in edit mode
      this.newCommunicationPoint.mode = this.newCommunicationPoint.mode?.toLowerCase() || '';
      this.newCommunicationPoint.type = this.newCommunicationPoint.type?.toLowerCase() || '';

      this.initializeDefaultConfigurations();
      this.initializeTypeSpecificDefaults();
    }
  }

  private initializeDefaultConfigurations(): void {
    if (!this.newCommunicationPoint.certificateDetails) {
      this.newCommunicationPoint.certificateDetails = {
        rootCertificatePath: '',
        clientCertificatePath: '',
        clientCertificatePassword: '',
      };
    }
    if (!this.newCommunicationPoint.proxyConfigurations) {
      this.newCommunicationPoint.proxyConfigurations = {
        isEnabled: false,
        proxyAddress: '',
        proxyPort: '',
        proxyUsername: '',
        proxyPassword: '',
      };
    }
    if (!this.newCommunicationPoint.connectionRetry) {
      this.newCommunicationPoint.connectionRetry = {
        retryAttempts: 0,
        baseRetryDelayInSeconds: 0,
      };
    }
    if (!this.newCommunicationPoint.messageRetry) {
      this.newCommunicationPoint.messageRetry = {
        retryAttempts: 0,
        baseRetryDelayInSeconds: 0,
      };
    }
  }

  private initializeTypeSpecificDefaults(): void {
    const type = this.newCommunicationPoint.type;

    if (type === 'cim_s3') {
      if (!this.newCommunicationPoint.s3Configuration) {
        this.newCommunicationPoint.s3Configuration = {
          serviceId: '',
          iamHost: '',
          gatewayUrl: '',
          tenantName: '',
          institutionName: '',
          timeout: 0,
          collectorId: '',
          timeZone: '',
          privateKeyPath: '',
          privateKeyPassword: ''
        };
      }
      if (!this.newCommunicationPoint.cacheConfiguration) {
        this.newCommunicationPoint.cacheConfiguration = {
          cacheReconciliationEndPoint: '',
          cacheRelaodEndPoint: '',
          cacheCertificatePath: '',
          cacheCertificatePassword: ''
        };
      }
    } else if (type === 'http' && !this.newCommunicationPoint.httpConfiguration) {
      this.newCommunicationPoint.httpConfiguration = {
        endPoint: ''
      };
    } else if (type === 'tcp' && !this.newCommunicationPoint.tcpConfiguration) {
      this.newCommunicationPoint.tcpConfiguration = {
        ipAddress: '',
        port: 0
      };
    } else if (type === 'websocket' && !this.newCommunicationPoint.webSocketConfiguration) {
      this.newCommunicationPoint.webSocketConfiguration = {
        endPoint: ''
      };
    }
  }


  isDialogOpen: boolean = false;
  isCertificationEnabled: boolean = false;
  ids: number = 4;
  newCommunicationPoint: CommunicationData = getDefaultCommunicationData();

  closeDialog() {
    this.isDialogOpen = false;
    this.close.emit(false);

  }

  submitCommunicationPoint() {
    this.uploadFiles();
    if (this.validateCommunicationPoint()) {
      if (!this.isEditMode) {
        this.addNewCommunicationPoint();
      } else {
        this.updateExistingCommunicationPoint();
      }
    }
  }



  private validateCommunicationPoint(): boolean {
    if (!this.validateRequiredFields()) {
      return false;
    }

    if (this.isS3Enabled()) {
      return this.validateS3Configuration();
    }

    if (!this.validateUniqueName()) {
      return false;
    }

    if (!this.validateTypeSpecificFields()) {
      return false;
    }

    if (!this.validateSSLFields()) {
      return false;
    }

    if (!this.validateProxyFields()) {
      return false;
    }

    return true;
  }

  private validateRequiredFields(): boolean {
    if (!this.newCommunicationPoint.name?.trim()) {
      this.errorValidation = 'Communication Point name is required.';
      return false;
    }
    if (!this.newCommunicationPoint.mode?.trim()) {
      this.errorValidation = 'Mode is required.';
      return false;
    }
    if (!this.newCommunicationPoint.type?.trim()) {
      this.errorValidation = 'Type is required.';
      return false;
    }
    return true;
  }

  private isS3Enabled(): boolean {
    return this.newCommunicationPoint.mode === 'output' && this.newCommunicationPoint.type === 'cim_s3';
  }

  private validateUniqueName(): boolean {
    if (
      this.nameSet.has(this.newCommunicationPoint.name) &&
      this.newCommunicationPoint.name !== this.currentCommunicationData.name
    ) {
      this.errorValidation = 'Name must be unique';
      return false;
    }
    return true;
  }

  private validateTypeSpecificFields(): boolean {
    const type = this.newCommunicationPoint.type;
    if (type === 'tcp') {
      if (!this.newCommunicationPoint.tcpConfiguration.ipAddress?.trim()) {
        this.errorValidation = 'IP is required.';
        return false;
      }
      if (!this.newCommunicationPoint.tcpConfiguration.port) {
        this.errorValidation = 'Port is required.';
        return false;
      }
    }

    if (type === 'http') {
      if (!this.newCommunicationPoint.httpConfiguration.endPoint?.trim()) {
        this.errorValidation = 'Endpoint is required.';
        return false;
      }
    }

    if (type === 'websocket') {
      if (!this.newCommunicationPoint.webSocketConfiguration.endPoint?.trim()) {
        this.errorValidation = 'WebSocket Endpoint is required.';
        return false;
      }
    }

    return true;
  }


  private validateSSLFields(): boolean {
    if (this.newCommunicationPoint.isSSLEnabled) {
      const cert = this.newCommunicationPoint.certificateDetails;
      if (!cert?.rootCertificatePath) {
        this.errorValidation = 'Root Certificate is required.';
        return false;
      }
      if (!cert?.clientCertificatePath) {
        this.errorValidation = 'Client Certificate is required.';
        return false;
      }
      if (!cert?.clientCertificatePassword) {
        this.errorValidation = 'Password is required.';
        return false;
      }
    }
    return true;
  }

  private validateProxyFields(): boolean {
    const proxy = this.newCommunicationPoint.proxyConfigurations;
    if (proxy?.isEnabled) {
      if (!proxy.proxyAddress) {
        this.errorValidation = 'Proxy Server is required.';
        return false;
      }
      if (!proxy.proxyPort) {
        this.errorValidation = 'Proxy Port is required.';
        return false;
      }
    }
    return true;
  }

  private validateS3Configuration(): boolean {
    const s3 = this.newCommunicationPoint.s3Configuration;
    const cache = this.newCommunicationPoint.cacheConfiguration;

    if (!s3.serviceId?.trim()) {
      this.errorValidation = 'serviceId is required.';
      return false;
    }
    if (!s3.tenantName?.trim()) {
      this.errorValidation = 'tenantName is required.';
      return false;
    }
    if (!s3.collectorId?.trim()) {
      this.errorValidation = 'collectorId is required.';
      return false;
    }
    if (!s3.institutionName?.trim()) {
      this.errorValidation = 'institutionName is required.';
      return false;
    }
    if (!s3.gatewayUrl?.trim()) {
      this.errorValidation = 'gatewayUrl is required.';
      return false;
    }
    if (!s3.iamHost?.trim()) {
      this.errorValidation = 'iamHost is required.';
      return false;
    }
    if (!s3.timeZone?.trim()) {
      this.errorValidation = 'timeZone is required.';
      return false;
    }
    if (!s3.privateKeyPath?.trim()) {
      this.errorValidation = 'privateKeyPath is required.';
      return false;
    }
    if (!s3.privateKeyPassword?.trim()) {
      this.errorValidation = 'privateKeyPassword is required.';
      return false;
    }
    if (!cache.cacheReconciliationEndPoint?.trim()) {
      this.errorValidation = 'CacheReconciliationEndPoint is required.';
      return false;
    }
    if (!cache.cacheRelaodEndPoint?.trim()) {
      this.errorValidation = 'CacheRelaodEndPoint is required.';
      return false;
    }
    if (!cache.cacheCertificatePath?.trim()) {
      this.errorValidation = 'CacheCertificatePath is required.';
      return false;
    }
    return true;
  }


  private addNewCommunicationPoint() {
    this.communicationDataService.addCommunicationData(this.newCommunicationPoint).subscribe({
      next: (response) => {
        this.successMessage = 'Communication point added successfully.';
      },
      error: (error) => {
        this.errorMessage = 'Error adding communication point: ' + error.message;
      },
      complete: () => {
        this.getAllCommunicationData.emit();
        this.closeDialog();
      }
    });
    this.isCertificateUploaded = false;
  }

  private updateExistingCommunicationPoint() {
    if (this.currentCommunicationData && this.currentCommunicationData.id > 0) {
      console.log('Updating communication point:', this.currentCommunicationData);
      this.communicationDataService.updateCommunicationData(this.currentCommunicationData.id, this.currentCommunicationData).subscribe({
        next: () => {
          console.log('Update successful');
          this.successMessage = `Communication point with ID ${this.currentCommunicationData.id} updated successfully.`;
        },
        error: (error) => {
          console.error('Error updating communication point:', error);
          this.errorMessage = 'Error updating communication point: ' + error.message;
        },
        complete: () => {
          console.log('Update operation completed');
          this.getAllCommunicationData.emit();
          this.closeDialog();
        }
      });
    } else {
      console.error('Invalid data for update');
      this.errorMessage = 'Please provide valid data to update';
    }
    this.isCertificateUploaded = false;
  }

  @HostListener('document:keydown.escape', ['$event'])
  handleEscapeKey(event: Event) {
    if (this.isDialogOpen) {
      this.closeDialog();
    }
  }

  trackByFn(index: number, item: any) {
    return index;
  }

  onOverlayClick(event: MouseEvent) {
    if ((event.target as HTMLElement).classList.contains('dialog-overlay')) {
      this.closeDialog();
    }
  }



  //Certificate Handling

  file1: File | null = null;
  file2: File | null = null;

  onFile1Selected(event: Event): void {
    const element = event.target as HTMLInputElement;
    this.file1 = element.files ? element.files[0] : null;
  }

  onFile2Selected(event: Event): void {
    const element = event.target as HTMLInputElement;
    this.file2 = element.files ? element.files[0] : null;
  }

  uploadFiles(): void {
    if (this.file1) {
      this.uploadFile(this.file1);
      if(this.newCommunicationPoint.certificateDetails.rootCertificatePath){
        this.deleteCertificates(this.newCommunicationPoint.certificateDetails.rootCertificatePath);
      }
      this.newCommunicationPoint.certificateDetails.rootCertificatePath = this.file1.name;
    }
    if (this.file2) {
      this.uploadFile(this.file2);
      this.deleteCertificates(this.newCommunicationPoint.certificateDetails.clientCertificatePath!);
      this.newCommunicationPoint.certificateDetails.clientCertificatePath = this.file2.name;
    }
    this.showCertificateDialog = false;
  }

  uploadFile(file: File): void {
    this.certificateService.uploadFile(file, this.newCommunicationPoint.name)
      .pipe(
        tap(response => console.log('Upload successful', response)),
        catchError(error => {
          console.error('Upload failed', error);
          return of(null);
        })
      )
      .subscribe();
    this.isCertificateUploaded = true;
  }

  deleteCertificates(fileName: string): void {
    if (fileName !== undefined && fileName !== null && fileName !== '') {
      this.certificateService.deleteFile(this.newCommunicationPoint.name, fileName)
        .pipe(
          tap(response => {
            console.log('Folder deleted successfully', response);
          }),
          catchError(error => {
            console.error('Folder deletion failed', error);
            return of(null);
          })
        )
        .subscribe();
    }
  }

  getFileName(path: string | undefined): string {

    if (path === undefined || path === null) {
      return "no file chosen";
    }
    return path ? path.split('\\').pop()?.split('/').pop() || path : 'No file chosen';
  }


  allowedTypes = {
    input: [
      { value: 'http', label: 'HTTP' },
      { value: 'tcp', label: 'TCP' },
      { value: 'websocket', label: 'Websocket' }
    ],
    output: [
      { value: 'http', label: 'HTTP' },
      { value: 'tcp', label: 'TCP' },
      { value: 'cim_s3', label: 'CIM_S3' }
    ]
  };

  getAllowedTypes(mode: string): { value: string; label: string }[] {
    if (mode === 'input') {
      return this.allowedTypes.input;
    } else if (mode === 'output') {
      return this.allowedTypes.output;
    }
    return [];
  }

}
