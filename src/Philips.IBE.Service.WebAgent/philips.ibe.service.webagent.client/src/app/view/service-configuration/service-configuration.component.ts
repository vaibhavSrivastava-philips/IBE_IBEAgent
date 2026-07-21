import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ButtonComponent,
  TextComponent,
  FlexBoxComponent,
  CardComponent,
  CardHeaderComponent,
  CardTitleComponent,
  CardFooterComponent
} from '@filament/angular';
import { ServiceNodeDialogComponent } from '../../components/service-node-dialog/service-node-dialog.component';
import { CommunicationDataService } from '../../services/communication-data.service';
import { tap, catchError, of } from 'rxjs';
import { CommunicationData } from '../../models/CommunicationData';
import { CertificateService } from '../../services/certificate.service';
import { NotificationService } from '../../services/notification.service';
import { ServiceNode } from '../../models/service-node';
import { NodeService } from '../../services/node.service';
import { ResponseModel } from '../../models/response-model';


@Component({
  selector: 'app-service-configuration',
  templateUrl: './service-configuration.component.html',
  styleUrls: ['./service-configuration.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    ButtonComponent,
    TextComponent,
    FlexBoxComponent,
    CardComponent,
    CardHeaderComponent,
    CardTitleComponent,
    CardFooterComponent,
    ServiceNodeDialogComponent
  ]
})
export class ServiceConfigurationComponent implements OnInit {

  id: number = 0;
  isDialogOpen: boolean = false;
  errorMessage: string = '';
  currentNodeType: string = '';
  tcp: ServiceNode = {
    ipAddress: '',
    port: 0,
    enableSSL: false,
    isEnabled: false,
    sslConfiguration: {
      serverCertificatePath: '',
      serverCertificatePassword: '',
      clientCertificatePassword: '',
      clientCertificatePath: '',
    }
  }
  httpServer: ServiceNode = {
    ipAddress: '',
    endPoint: '',
    port: 0,
    enableSSL: false,
    isEnabled: false,
    sslConfiguration: {
      serverCertificatePath: '',
      serverCertificatePassword: '',
      clientCertificatePassword: '',
      clientCertificatePath: '',
      rootCertificatePath: ''
    }
  }
  webSocket: ServiceNode = {
    endPoint: '',
    port: 0,
    contextPath: '',
    enableSSL: false,
    isEnabled: false,
    sslConfiguration: {
      serverCertificatePath: '',
      serverCertificatePassword: '',
      clientCertificatePassword: '',
      clientCertificatePath: '',
    },
    proxyConfigurations: {
      isEnabled: false,
      proxyAddress: '',
      proxyPort: '',
      proxyUsername: '',
      proxyPassword: ''
    },
    connectionRetry: {
      retryAttempts: 0,
      baseRetryDelayInSeconds: 0
    }
  }
  adt: ServiceNode = {
    ipAddress: '',
    port: 0,
    enableSSL: false,
    isEnabled: false
  }

  constructor(
    private nodeService: NodeService,
    private certificateService: CertificateService,
    private readonly notificationService: NotificationService,
    private readonly cdr: ChangeDetectorRef
  ) {
    this.reset();
  }

  currentNode!: ServiceNode;
  isEditMode: boolean = false;
  ngOnInit() {
    this.getServiceNode();
  }


  getServiceNode(): void {
    const initializeNode = (data: any): ServiceNode => {
      return {
        endPoint: data?.endPoint ?? '',
        ipAddress: data?.ipAddress ?? '',
        port: data?.port ?? 0,
        contextPath: data?.contextPath ?? '',
        enableSSL: data?.enableSSL ?? false,
        isEnabled: data?.isEnabled ?? false,
        sslConfiguration: {
          serverCertificatePath: data?.sslConfiguration?.serverCertificatePath ?? null,
          serverCertificatePassword: data?.sslConfiguration?.serverCertificatePassword ?? null,
          clientCertificatePassword: data?.sslConfiguration?.clientCertificatePassword ?? null,
          clientCertificatePath: data?.sslConfiguration?.clientCertificatePath ?? null,
          ...(data?.sslConfiguration?.hasOwnProperty('rootCertificatePath') && {
        rootCertificatePath: data?.sslConfiguration?.rootCertificatePath ?? null
          })
        },
        proxyConfigurations: {
          isEnabled: data?.proxyConfigurations?.isEnabled ?? false,
          proxyAddress: data?.proxyConfigurations?.proxyAddress ?? '',
          proxyPort: data?.proxyConfigurations?.proxyPort ?? '',
          proxyUsername: data?.proxyConfigurations?.proxyUsername ?? '',
          proxyPassword: data?.proxyConfigurations?.proxyPassword ?? ''
        },
        connectionRetry: {
          retryAttempts: data?.connectionRetry?.retryAttempts ?? 0,
          baseRetryDelayInSeconds: data?.connectionRetry?.baseRetryDelayInSeconds ?? 0
        }
      };
    };
    this.nodeService.getServiceNodes().subscribe({
      next: (data) => {
        this.tcp = initializeNode(data?.tcp);
        this.httpServer = initializeNode(data?.http);
        this.webSocket = initializeNode(data?.webSocketClient);
        this.adt = initializeNode(data?.adt);
        this.cdr.detectChanges();
      },
      error: error => {
        this.errorMessage = error;
      }
    }
    )
  }

  reset() {
    this.currentNode = {
      endPoint: '',
      port: 0,
      contextPath: '',
      enableSSL: false,
      isEnabled: false,
      sslConfiguration: {
        serverCertificatePath: '',
        serverCertificatePassword: '',
        clientCertificatePassword: '',
        clientCertificatePath: '',
      },
      proxyConfigurations: {
        isEnabled: false,
        proxyAddress: '',
        proxyPort: '',
        proxyUsername: '',
        proxyPassword: ''
      },
      connectionRetry: {
        retryAttempts: 0,
        baseRetryDelayInSeconds: 0
      }
    };
  }

  //Dialog Methods
  openDialog(type: string, flag: boolean) {
    this.currentNodeType = type;
    if (type === 'tcp') {
      this.currentNode = JSON.parse(JSON.stringify(this.tcp));
    } else if (type === 'httpServer') {
      this.currentNode = JSON.parse(JSON.stringify(this.httpServer));
    } else if (type === 'webSocket') {
      this.currentNode = JSON.parse(JSON.stringify(this.webSocket));
    } else if (type === 'adt')
      this.currentNode = JSON.parse(JSON.stringify(this.adt));
    this.isDialogOpen = true;
  }

  closeDialog(response: ResponseModel) {
    if (response.status === 0) {
      if (this.currentNodeType === 'tcp') {
        this.updateTCPNode(response.value);
      } else if (this.currentNodeType === 'httpServer') {
        this.updateHTTPServerNode(response.value);
      } else if (this.currentNodeType === 'webSocket') {
        this.updateWebSocketNode(response.value);
      } else if (this.currentNodeType === 'adt') {
        this.updateADTNode(response.value);
      }
    }
    this.isEditMode = false;
    this.isDialogOpen = false;
  }



  updateTCPNode(node: ServiceNode) {
    this.nodeService.updateTCPNode(node).subscribe(
      {
        next: (data) => {
          this.notificationService.showMessage('success', 'TCP Node updated successfully', 'Success');
          this.getServiceNode();
        },
        error: error => {
          this.notificationService.showMessage('error', 'TCP Node update failed', error.message);
        }
      }
    )
  }

  updateWebSocketNode(value: any) {
    this.nodeService.updateWebSocketNode(value).subscribe({
      next: (data) => {
        this.notificationService.showMessage('success', 'WebSocket Node updated successfully', 'Success');
        this.getServiceNode();
      },
      error: error => {
        this.notificationService.showMessage('error', 'WebSocket Node update failed', error.message);
      }
    });
  }

  updateHTTPServerNode(value: any) {
    this.nodeService.updateHTTPServerNode(value).subscribe({
      next: (data) => {
        this.notificationService.showMessage('success', 'HTTP Server Node updated successfully', 'Success');
        this.getServiceNode();
      },
      error: error => {
        this.notificationService.showMessage('error', 'HTTP Server Node update failed', error.message);
      }
    });
  }
  updateADTNode(node: ServiceNode) {
    this.nodeService.updateADTNode(node).subscribe(
      {
        next: (data) => {
          this.notificationService.showMessage('success', 'ADT Node updated successfully', 'Success');
          this.getServiceNode();
        },
        error: error => {
          this.notificationService.showMessage('error', 'ADT Node update failed', error.message);
        }
      }
    )
  }
}
