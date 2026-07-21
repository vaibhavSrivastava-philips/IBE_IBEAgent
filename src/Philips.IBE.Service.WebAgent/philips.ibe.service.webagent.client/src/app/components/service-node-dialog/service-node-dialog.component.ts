import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ButtonComponent,
  DialogComponent,
  DialogTitleComponent,
  DialogContentComponent,
  DialogActionsComponent,
  DataGridComponent,
  ToggleSwitchComponent,
  FlexBoxComponent,
  InputDirective
} from '@filament/angular';
import { ServiceNode } from '../../models/service-node';
import { CertificateService } from '../../services/certificate.service';
import { tap, catchError, of } from 'rxjs';
import { ResponseModel } from '../../models/response-model';

@Component({
  selector: 'app-service-node-dialog',
  templateUrl: './service-node-dialog.component.html',
  styleUrls: ['./service-node-dialog.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonComponent,
    DialogComponent,
    DialogTitleComponent,
    DialogContentComponent,
    DialogActionsComponent,
    DataGridComponent,
    ToggleSwitchComponent,
    FlexBoxComponent,
    InputDirective
  ]
})
export class ServiceNodeDialogComponent implements OnInit {


  @Output() close = new EventEmitter<ResponseModel>();
  @Input() type = "";
  @Input() inputData: ServiceNode | undefined;
  isEditMode: boolean = true;
  serviceNode: ServiceNode = {
    enableSSL: false,
    isEnabled: false,
    endPoint: '',
    ipAddress: '',
    sslConfiguration: {
      serverCertificatePath: '',
      serverCertificatePassword: '',
      clientCertificatePassword: '',
      clientCertificatePath: ''
    },
    proxyConfigurations: {
      isEnabled: false,
      proxyAddress: '',
      proxyPassword: '',
      proxyPort: '',
      proxyUsername: ''
    },
    connectionRetry: {
      baseRetryDelayInSeconds: 0,
      retryAttempts: 0
    }
  }
  isCertificateUploaded: boolean = false;
  ngOnInit(): void {
    if (this.inputData === undefined) {
      this.isEditMode = false;
    } else {
      this.isEditMode = true;
      this.serviceNode = this.inputData;
      console.log("Service Input Data:", this.inputData);
      if (this.serviceNode.sslConfiguration) {
        this.isCertificateUploaded = true;
      }
    }
  }

  constructor(
    private certificateService: CertificateService,
  ) {

  }

  UpdateServiceNode() {
    if (this.root !== null && this.inputData?.sslConfiguration?.rootCertificatePath !== null && this.inputData?.sslConfiguration?.rootCertificatePath !== undefined) {
      this.updateCertificateDetails(this.root, this.inputData!.sslConfiguration.rootCertificatePath);
      this.inputData!.sslConfiguration!.rootCertificatePath = this.root.name;
    }
     if (this.server !== null) {
       this.updateCertificateDetails(this.server, this.inputData!.sslConfiguration!.serverCertificatePath!);
       this.inputData!.sslConfiguration!.serverCertificatePath = this.server.name;
     }
    if(this.client !== null){
      this.updateCertificateDetails(this.client, this.inputData!.sslConfiguration!.clientCertificatePath!);
      this.inputData!.sslConfiguration!.clientCertificatePath = this.client.name;
    }

    console.log("This is service node", this.serviceNode);
    let response: ResponseModel = {
      'value': this.serviceNode,
      'displayMessage': this.type,
      'status': 0
    }
    this.close.emit(response)
  }

  private updateCertificateDetails(file:File, file2:string): void {
    this.uploadFile(file);
    if(file2!==undefined && file2!==null && file2!=='')
      this.deleteCertificates(file2); 
  }

  dismissForAlert() {
    let response: ResponseModel = {
      'value': this.serviceNode,
      'displayMessage': this.type,
      'status': 1
    }
    this.close.emit(response)
  }

  //Certificate Handling

  root: File | null = null;
  server: File | null = null;
  client: File | null = null;

  onClientCertificateFileSelected(event: Event): void {
    const element = event.target as HTMLInputElement;
    this.client = element.files ? element.files[0] : null;
  }

  onRootCertificateFileSelected(event: Event): void {
    const element = event.target as HTMLInputElement;
    this.root = element.files ? element.files[0] : null;
  }

  onServerCertificateFileSelected(event: Event): void {
    const element = event.target as HTMLInputElement;
    this.server = element.files ? element.files[0] : null;
  }

  uploadFile(file: File): void {
    this.certificateService.uploadFile(file, this.type + '-service')
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
    this.certificateService.deleteFile(this.type + '-service', fileName)
      .pipe(
        tap(response => {
          console.log('Folder deleted successfully', response);
          this.inputData!.sslConfiguration!.rootCertificatePath = '';
          this.inputData!.sslConfiguration!.serverCertificatePath = '';
          this.inputData!.sslConfiguration!.clientCertificatePath = '';
        }),
        catchError(error => {
          console.error('Folder deletion failed', error);
          return of(null);
        })
      )
      .subscribe();

  }

  getFileName(path: string | undefined): string {
    if (path === null || path === undefined) {
      return 'No file chosen';
    }
    return path ? path.split('\\').pop()?.split('/').pop() || path : 'No file chosen';
  }

}
