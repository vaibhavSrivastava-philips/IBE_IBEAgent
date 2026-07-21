import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { CompointDialogComponent } from './compoint-dialog.component';
import { CommunicationDataService } from '../../services/communication-data.service';
import { CertificateService } from '../../services/certificate.service';
import { getDefaultCommunicationData } from '../../models/CommunicationComponent';
import { of, throwError } from 'rxjs';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { FormsModule } from '@angular/forms';

describe('CompointDialogComponent', () => {
  let component: CompointDialogComponent;
  let fixture: ComponentFixture<CompointDialogComponent>;
  let communicationDataServiceSpy: jasmine.SpyObj<CommunicationDataService>;
  let certificateServiceSpy: jasmine.SpyObj<CertificateService>;
  const TEST_CREDENTIAL = 'test-value';

  beforeEach(async () => {
    communicationDataServiceSpy = jasmine.createSpyObj('CommunicationDataService', ['addCommunicationData', 'updateCommunicationData']);
    certificateServiceSpy = jasmine.createSpyObj('CertificateService', ['uploadFile', 'deleteFile']);

    await TestBed.configureTestingModule({
      declarations: [CompointDialogComponent],
      imports: [FormsModule],
      providers: [
        { provide: CommunicationDataService, useValue: communicationDataServiceSpy },
        { provide: CertificateService, useValue: certificateServiceSpy }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();

    fixture = TestBed.createComponent(CompointDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize newCommunicationPoint on ngOnInit if name is not empty', () => {
    component.currentCommunicationData = { ...getDefaultCommunicationData(), name: 'test' };
    component.ngOnInit();
    expect(component.newCommunicationPoint.name).toBe('test');
  });

  it('should close dialog and emit close event', () => {
    spyOn(component.close, 'emit');
    component.isDialogOpen = true;
    component.closeDialog();
    expect(component.isDialogOpen).toBeFalse();
    expect(component.close.emit).toHaveBeenCalledWith(false);
  });

  it('should emit getAllCommunicationData and close dialog after addNewCommunicationPoint', fakeAsync(() => {
    spyOn(component.getAllCommunicationData, 'emit');
    spyOn(component, 'closeDialog');
    communicationDataServiceSpy.addCommunicationData.and.returnValue(of('success'));
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), name: 'test' };
    component['addNewCommunicationPoint']();
    tick();
    expect(component.successMessage).toContain('added successfully');
    expect(component.getAllCommunicationData.emit).toHaveBeenCalled();
    expect(component.closeDialog).toHaveBeenCalled();
  }));

  it('should set errorMessage on addNewCommunicationPoint error', fakeAsync(() => {
    communicationDataServiceSpy.addCommunicationData.and.returnValue(throwError(() => new Error('fail')));
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), name: 'test' };
    component['addNewCommunicationPoint']();
    tick();
    expect(component.errorMessage).toContain('Error adding communication point');
  }));

  it('should update existing communication point if id > 0', fakeAsync(() => {
    spyOn(component.getAllCommunicationData, 'emit');
    spyOn(component, 'closeDialog');
    communicationDataServiceSpy.updateCommunicationData.and.returnValue(of({}));
    component.currentCommunicationData = { ...getDefaultCommunicationData(), id: 1, name: 'test' };
    component['updateExistingCommunicationPoint']();
    tick();
    expect(component.successMessage).toContain('updated successfully');
    expect(component.getAllCommunicationData.emit).toHaveBeenCalled();
    expect(component.closeDialog).toHaveBeenCalled();
  }));

  it('should set errorMessage if updateExistingCommunicationPoint called with invalid id', () => {
    component.currentCommunicationData = { ...getDefaultCommunicationData(), id: 0, name: 'test' };
    component['updateExistingCommunicationPoint']();
    expect(component.errorMessage).toContain('Please provide valid data to update');
  });

  it('should validate required fields', () => {
    component.newCommunicationPoint = getDefaultCommunicationData();
    expect(component['validateRequiredFields']()).toBeFalse();
    component.newCommunicationPoint.name = 'test';
    expect(component['validateRequiredFields']()).toBeFalse();
    component.newCommunicationPoint.mode = 'input';
    expect(component['validateRequiredFields']()).toBeFalse();
    component.newCommunicationPoint.type = 'http';
    expect(component['validateRequiredFields']()).toBeTrue();
  });

  it('should validate unique name', () => {
    component.nameSet = new Set(['existing']);
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), name: 'existing' };
    component.currentCommunicationData = { ...getDefaultCommunicationData(), name: 'other' };
    expect(component['validateUniqueName']()).toBeFalse();
    component.currentCommunicationData = { ...getDefaultCommunicationData(), name: 'existing' };
    expect(component['validateUniqueName']()).toBeTrue();
  });

  it('should validate type specific fields for tcp', () => {
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), type: 'tcp', tcpConfiguration: { ipAddress: '', port: 0 } };
    expect(component['validateTypeSpecificFields']()).toBeFalse();
    component.newCommunicationPoint.tcpConfiguration.ipAddress = '127.0.0.1';
    expect(component['validateTypeSpecificFields']()).toBeFalse();
    component.newCommunicationPoint.tcpConfiguration.port = 1234;
    expect(component['validateTypeSpecificFields']()).toBeTrue();
  });

  it('should validate type specific fields for http', () => {
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), type: 'http', httpConfiguration: { endPoint: '' } };
    expect(component['validateTypeSpecificFields']()).toBeFalse();
    component.newCommunicationPoint.httpConfiguration.endPoint = 'https://test';
    expect(component['validateTypeSpecificFields']()).toBeTrue();
  });

  it('should validate SSL fields', () => {
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), isSSLEnabled: true, certificateDetails: { rootCertificatePath: '', clientCertificatePath: '', clientCertificatePassword: '' } };
    expect(component['validateSSLFields']()).toBeFalse();
    component.newCommunicationPoint.certificateDetails.rootCertificatePath = 'root.pem';
    expect(component['validateSSLFields']()).toBeFalse();
    component.newCommunicationPoint.certificateDetails.clientCertificatePath = 'client.pem';
    expect(component['validateSSLFields']()).toBeFalse();
    component.newCommunicationPoint.certificateDetails.clientCertificatePassword = TEST_CREDENTIAL;
    expect(component['validateSSLFields']()).toBeTrue();
  });

  it('should validate proxy fields', () => {
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), proxyConfigurations: { isEnabled: true, proxyAddress: '', proxyPort: '' } };
    expect(component['validateProxyFields']()).toBeFalse();
    component.newCommunicationPoint.proxyConfigurations.proxyAddress = 'address';
    expect(component['validateProxyFields']()).toBeFalse();
    component.newCommunicationPoint.proxyConfigurations.proxyPort = '8080';
    expect(component['validateProxyFields']()).toBeTrue();
  });

  it('should validate S3 configuration', () => {
    const s3 = {
      serviceId: '',
      tenantName: '',
      collectorId: '',
      institutionName: '',
      gatewayUrl: '',
      iamHost: '',
      timeZone: '',
      privateKeyPath: '',
      privateKeyPassword: ''
    };
    const cache = {
      cacheReconciliationEndPoint: '',
      cacheRelaodEndPoint: '',
      cacheCertificatePath: '',
      cacheCertificatePassword: ''
    };
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), s3Configuration: s3, cacheConfiguration: cache };
    expect(component['validateS3Configuration']()).toBeFalse();
    s3.serviceId = 'svc'; s3.tenantName = 'tenant'; s3.collectorId = 'col'; s3.institutionName = 'inst'; s3.gatewayUrl = 'url'; s3.iamHost = 'host'; s3.timeZone = 'tz'; s3.privateKeyPath = 'pkpath'; s3.privateKeyPassword = TEST_CREDENTIAL;
    cache.cacheReconciliationEndPoint = 'a'; cache.cacheRelaodEndPoint = 'b'; cache.cacheCertificatePath = 'c'; cache.cacheCertificatePassword = TEST_CREDENTIAL;
    expect(component['validateS3Configuration']()).toBeTrue();
  });

  it('should call uploadFile and deleteCertificates in uploadFiles', () => {
    const file = new File([''], 'root.pem');
    component.file1 = file;
    component.file2 = file;
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), certificateDetails: { rootCertificatePath: '', clientCertificatePath: '', clientCertificatePassword: '' } };
    certificateServiceSpy.uploadFile.and.returnValue(of({}));
    certificateServiceSpy.deleteFile.and.returnValue(of({}));
    spyOn(component, 'uploadFile').and.callThrough();
    spyOn(component, 'deleteCertificates').and.callThrough();
    component.uploadFiles();
    expect(component.uploadFile).toHaveBeenCalledTimes(2);
    expect(component.deleteCertificates).toHaveBeenCalledTimes(2);
    expect(component.newCommunicationPoint.certificateDetails.rootCertificatePath).toBe('root.pem');
    expect(component.newCommunicationPoint.certificateDetails.clientCertificatePath).toBe('root.pem');
  });

  it('should call certificateService.uploadFile in uploadFile', () => {
    const file = new File([''], 'root.pem');
    certificateServiceSpy.uploadFile.and.returnValue(of({}));
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), name: 'test' };
    component.uploadFile(file);
    expect(certificateServiceSpy.uploadFile).toHaveBeenCalledWith(file, 'test');
    expect(component.isCertificateUploaded).toBeTrue();
  });

  it('should call certificateService.deleteFile in deleteCertificates', () => {
    certificateServiceSpy.deleteFile.and.returnValue(of({}));
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), name: 'test' };
    component.deleteCertificates('file.pem');
    expect(certificateServiceSpy.deleteFile).toHaveBeenCalledWith('test', 'file.pem');
  });

  it('should not call deleteFile if fileName is empty', () => {
    certificateServiceSpy.deleteFile.and.returnValue(of({}));
    component.newCommunicationPoint = { ...getDefaultCommunicationData(), name: 'test' };
    component.deleteCertificates('');
    expect(certificateServiceSpy.deleteFile).not.toHaveBeenCalled();
  });

  it('should return file name from getFileName', () => {
    expect(component.getFileName(undefined)).toBe('no file chosen');
    expect(component.getFileName('')).toBe('No file chosen');
    expect(component.getFileName('C:\\folder\\file.txt')).toBe('file.txt');
    expect(component.getFileName('/folder/file.txt')).toBe('file.txt');
  });

  it('should return allowed types for input and output', () => {
    expect(component.getAllowedTypes('input').length).toBeGreaterThan(0);
    expect(component.getAllowedTypes('output').length).toBeGreaterThan(0);
    expect(component.getAllowedTypes('other')).toEqual([]);
  });

  it('should close dialog on escape key if open', () => {
    spyOn(component, 'closeDialog');
    component.isDialogOpen = true;
    component.handleEscapeKey(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(component.closeDialog).toHaveBeenCalled();
  });

  it('should close dialog on overlay click', () => {
    spyOn(component, 'closeDialog');
    const div = document.createElement('div');
    div.classList.add('dialog-overlay');
    const event = new MouseEvent('click', { bubbles: true });
    Object.defineProperty(event, 'target', { value: div, writable: false });
    component.onOverlayClick(event);
    expect(component.closeDialog).toHaveBeenCalled();
  });


  it('should not close dialog if overlay not clicked', () => {
    spyOn(component, 'closeDialog');
    const div = document.createElement('div');
    const event = new MouseEvent('click', { bubbles: true });
    Object.defineProperty(event, 'target', { value: div, writable: false });
    component.onOverlayClick(event);
    expect(component.closeDialog).not.toHaveBeenCalled();
  });

});
