import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ServiceConfigurationComponent } from './service-configuration.component';
import { NodeService } from '../../services/node.service';
import { CertificateService } from '../../services/certificate.service';
import { NotificationService } from '../../services/notification.service';
import { of, throwError } from 'rxjs';
import { ResponseModel } from '../../models/response-model';

describe('ServiceConfigurationComponent', () => {
  let component: ServiceConfigurationComponent;
  let fixture: ComponentFixture<ServiceConfigurationComponent>;
  let nodeServiceSpy: jasmine.SpyObj<NodeService>;
  let certificateServiceSpy: jasmine.SpyObj<CertificateService>;
  let notificationServiceSpy: jasmine.SpyObj<NotificationService>;

  beforeEach(async () => {
    nodeServiceSpy = jasmine.createSpyObj('NodeService', [
      'getServiceNodes',
      'updateTCPNode',
      'updateWebSocketNode',
      'updateHTTPServerNode',
      'updateADTNode'
    ]);
    certificateServiceSpy = jasmine.createSpyObj('CertificateService', ['dummy']); 
    notificationServiceSpy = jasmine.createSpyObj('NotificationService', ['showMessage']);

    await TestBed.configureTestingModule({
      declarations: [ServiceConfigurationComponent],
      providers: [
        { provide: NodeService, useValue: nodeServiceSpy },
        { provide: CertificateService, useValue: certificateServiceSpy },
        { provide: NotificationService, useValue: notificationServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ServiceConfigurationComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call getServiceNode on init', () => {
    spyOn(component, 'getServiceNode');
    component.ngOnInit();
    expect(component.getServiceNode).toHaveBeenCalled();
  });

  it('should set nodes on getServiceNode success', () => {
    const mockData = {
      tcp: { port: 123, enableSSL: true, isEnabled: true },
      http: { port: 456, enableSSL: false, isEnabled: false },
      webSocketClient: { port: 789, enableSSL: true, isEnabled: true },
      adt: { port: 321, enableSSL: false, isEnabled: false }
    };
    nodeServiceSpy.getServiceNodes.and.returnValue(of(mockData));
    component.getServiceNode();
    expect(component.tcp.port).toBe(123);
    expect(component.httpServer.port).toBe(456);
    expect(component.webSocket.port).toBe(789);
    expect(component.adt.port).toBe(321);
  });

  it('should set errorMessage on getServiceNode error', () => {
    nodeServiceSpy.getServiceNodes.and.returnValue(throwError(() => 'error'));
    component.getServiceNode();
    expect(component.errorMessage).toBe('error');
  });

  it('should reset currentNode', () => {
    component.currentNode = { endPoint: 'test', port: 1, enableSSL: true, isEnabled: true } as any;
    component.reset();
    expect(component.currentNode.port).toBe(0);
    expect(component.currentNode.enableSSL).toBe(false);
  });

  it('should open dialog and set currentNode for tcp', () => {
    component.tcp.port = 111;
    component.openDialog('tcp', true);
    expect(component.currentNode.port).toBe(111);
    expect(component.isDialogOpen).toBeTrue();
    expect(component.currentNodeType).toBe('tcp');
  });

  it('should open dialog and set currentNode for httpServer', () => {
    component.httpServer.port = 222;
    component.openDialog('httpServer', true);
    expect(component.currentNode.port).toBe(222);
    expect(component.isDialogOpen).toBeTrue();
    expect(component.currentNodeType).toBe('httpServer');
  });

  it('should open dialog and set currentNode for webSocket', () => {
    component.webSocket.port = 333;
    component.openDialog('webSocket', true);
    expect(component.currentNode.port).toBe(333);
    expect(component.isDialogOpen).toBeTrue();
    expect(component.currentNodeType).toBe('webSocket');
  });

  it('should open dialog and set currentNode for adt', () => {
    component.adt.port = 444;
    component.openDialog('adt', true);
    expect(component.currentNode.port).toBe(444);
    expect(component.isDialogOpen).toBeTrue();
    expect(component.currentNodeType).toBe('adt');
  });

  it('should call updateTCPNode on closeDialog for tcp', () => {
    spyOn(component, 'updateTCPNode');
    component.currentNodeType = 'tcp';
    component.closeDialog({ status: 0, value: {}, displayMessage: '' } as ResponseModel);
    expect(component.updateTCPNode).toHaveBeenCalled();
    expect(component.isDialogOpen).toBeFalse();
    expect(component.isEditMode).toBeFalse();
  });

  it('should call updateHTTPServerNode on closeDialog for httpServer', () => {
    spyOn(component, 'updateHTTPServerNode');
    component.currentNodeType = 'httpServer';
    component.closeDialog({ status: 0, value: {}, displayMessage: '' } as ResponseModel);
    expect(component.updateHTTPServerNode).toHaveBeenCalled();
    expect(component.isDialogOpen).toBeFalse();
    expect(component.isEditMode).toBeFalse();
  });

  it('should call updateWebSocketNode on closeDialog for webSocket', () => {
    spyOn(component, 'updateWebSocketNode');
    component.currentNodeType = 'webSocket';
    component.closeDialog({ status: 0, value: {}, displayMessage: '' } as ResponseModel);
    expect(component.updateWebSocketNode).toHaveBeenCalled();
    expect(component.isDialogOpen).toBeFalse();
    expect(component.isEditMode).toBeFalse();
  });

  it('should call updateADTNode on closeDialog for adt', () => {
    spyOn(component, 'updateADTNode');
    component.currentNodeType = 'adt';
    component.closeDialog({ status: 0, value: {}, displayMessage: '' } as ResponseModel);
    expect(component.updateADTNode).toHaveBeenCalled();
    expect(component.isDialogOpen).toBeFalse();
    expect(component.isEditMode).toBeFalse();
  });

  it('should update TCP node and show success message', () => {
    nodeServiceSpy.updateTCPNode.and.returnValue(of({}));
    spyOn(component, 'getServiceNode');
    component.updateTCPNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('success', 'TCP Node updated successfully', 'Success');
    expect(component.getServiceNode).toHaveBeenCalled();
  });

  it('should show error message on TCP node update failure', () => {
    nodeServiceSpy.updateTCPNode.and.returnValue(throwError(() => ({ message: 'fail' })));
    component.updateTCPNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'TCP Node update failed', 'fail');
  });

  it('should update WebSocket node and show success message', () => {
    nodeServiceSpy.updateWebSocketNode.and.returnValue(of({}));
    spyOn(component, 'getServiceNode');
    component.updateWebSocketNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('success', 'WebSocket Node updated successfully', 'Success');
    expect(component.getServiceNode).toHaveBeenCalled();
  });

  it('should show error message on WebSocket node update failure', () => {
    nodeServiceSpy.updateWebSocketNode.and.returnValue(throwError(() => ({ message: 'fail' })));
    component.updateWebSocketNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'WebSocket Node update failed', 'fail');
  });

  it('should update HTTP Server node and show success message', () => {
    nodeServiceSpy.updateHTTPServerNode.and.returnValue(of({}));
    spyOn(component, 'getServiceNode');
    component.updateHTTPServerNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('success', 'HTTP Server Node updated successfully', 'Success');
    expect(component.getServiceNode).toHaveBeenCalled();
  });

  it('should show error message on HTTP Server node update failure', () => {
    nodeServiceSpy.updateHTTPServerNode.and.returnValue(throwError(() => ({ message: 'fail' })));
    component.updateHTTPServerNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'HTTP Server Node update failed', 'fail');
  });

  it('should update ADT node and show success message', () => {
    nodeServiceSpy.updateADTNode.and.returnValue(of({}));
    spyOn(component, 'getServiceNode');
    component.updateADTNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('success', 'ADT Node updated successfully', 'Success');
    expect(component.getServiceNode).toHaveBeenCalled();
  });

  it('should show error message on ADT node update failure', () => {
    nodeServiceSpy.updateADTNode.and.returnValue(throwError(() => ({ message: 'fail' })));
    component.updateADTNode({} as any);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'ADT Node update failed', 'fail');
  });
});
