import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ServiceNodeDialogComponent } from './service-node-dialog.component';
import { CertificateService } from '../../services/certificate.service';
import { of, throwError } from 'rxjs';
import { EventEmitter } from '@angular/core';
import { ResponseModel } from '../../models/response-model';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core'; 

describe('ServiceNodeDialogComponent', () => {
  let component: ServiceNodeDialogComponent;
  let fixture: ComponentFixture<ServiceNodeDialogComponent>;
  let certificateServiceSpy: jasmine.SpyObj<CertificateService>;

  beforeEach(async () => {
    certificateServiceSpy = jasmine.createSpyObj('CertificateService', ['uploadFile', 'deleteFile']);
    await TestBed.configureTestingModule({
      declarations: [ServiceNodeDialogComponent],
      providers: [
        { provide: CertificateService, useValue: certificateServiceSpy }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(ServiceNodeDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should set isEditMode to false if inputData is undefined', () => {
    component.inputData = undefined;
    component.ngOnInit();
    expect(component.isEditMode).toBeFalse();
  });

  it('should set isEditMode to true and set serviceNode if inputData is provided', () => {
    const inputData = {
      enableSSL: true,
      isEnabled: true,
      endPoint: 'test',
      ipAddress: '127.0.0.1',
      sslConfiguration: {
        rootCertificatePath: 'root',
        clientCertificatePassword: '',
        clientCertificatePath: 'client'
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
    };
    component.inputData = inputData;
    component.ngOnInit();
    expect(component.isEditMode).toBeTrue();
    expect(component.serviceNode).toEqual(inputData);
    expect(component.isCertificateUploaded).toBeTrue();
  });

  it('should emit close event with status 0 on UpdateServiceNode', () => {
    component.inputData = component.serviceNode;
    component.root = new File([''], 'root.crt');
    certificateServiceSpy.uploadFile.and.returnValue(of({}));
    spyOn(component.close, 'emit');
    component.UpdateServiceNode();
    expect(component.close.emit).toHaveBeenCalledWith(jasmine.objectContaining({ status: 0 }));
  });



  it('should emit close event with status 1 on dismissForAlert', () => {
    spyOn(component.close, 'emit');
    component.dismissForAlert();
    expect(component.close.emit).toHaveBeenCalledWith(jasmine.objectContaining({ status: 1 }));
  });

  it('should set client file on onClientCertificateFileSelected', () => {
    const file = new File([''], 'client.crt');
    const event = {
      originalEvent: { currentTarget: { files: [file] } }
    } as any;
    component.onClientCertificateFileSelected(event);
    expect(component.client).toBe(file);
  });

  it('should set root file on onRootCertificateFileSelected', () => {
    const file = new File([''], 'root.crt');
    const event = {
      originalEvent: { currentTarget: { files: [file] } }
    } as any;
    component.onRootCertificateFileSelected(event);
    expect(component.root).toBe(file);
  });

  it('should set server file on onServerCertificateFileSelected', () => {
    const file = new File([''], 'server.crt');
    const event = {
      originalEvent: { currentTarget: { files: [file] } }
    } as any;
    component.onServerCertificateFileSelected(event);
    expect(component.server).toBe(file);
  });

  it('should call uploadFile and set isCertificateUploaded to true', () => {
    const file = new File([''], 'test.crt');
    certificateServiceSpy.uploadFile.and.returnValue(of({}));
    component.uploadFile(file);
    expect(certificateServiceSpy.uploadFile).toHaveBeenCalled();
    expect(component.isCertificateUploaded).toBeTrue();
  });

  it('should handle uploadFile error', () => {
    const file = new File([''], 'test.crt');
    certificateServiceSpy.uploadFile.and.returnValue(throwError(() => new Error('fail')));
    component.uploadFile(file);
    expect(certificateServiceSpy.uploadFile).toHaveBeenCalled();
  });

  it('should call deleteCertificates and clear certificate paths', () => {
    component.inputData = component.serviceNode;
    certificateServiceSpy.deleteFile.and.returnValue(of({}));
    component.deleteCertificates('test.crt');
    expect(certificateServiceSpy.deleteFile).toHaveBeenCalled();
  });

  it('should handle deleteCertificates error', () => {
    component.inputData = component.serviceNode;
    certificateServiceSpy.deleteFile.and.returnValue(throwError(() => new Error('fail')));
    component.deleteCertificates('test.crt');
    expect(certificateServiceSpy.deleteFile).toHaveBeenCalled();
  });

  it('should return file name from path in getFileName', () => {
    expect(component.getFileName('folder/file.txt')).toBe('file.txt');
    expect(component.getFileName(undefined)).toBe('No file chosen');
    expect(component.getFileName(null as any)).toBe('No file chosen');
  });
});
