import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CommunicationPointComponent } from './communication-point.component';
import { CommunicationDataService } from '../../services/communication-data.service';
import { CertificateService } from '../../services/certificate.service';
import { NotificationService } from '../../services/notification.service';
import { ContractsService } from '../../services/contracts.service';
import { of, throwError } from 'rxjs';
import { getDefaultCommunicationData } from '../../models/CommunicationComponent';
import { TableModule } from 'primeng/table';

describe('CommunicationPointComponent', () => {
  let component: CommunicationPointComponent;
  let fixture: ComponentFixture<CommunicationPointComponent>;
  let mockCommunicationDataService: any;
  let mockCertificateService: any;
  let mockNotificationService: any;
  let mockContractsService: any;

  beforeEach(async () => {
    mockCommunicationDataService = {
      getAllCommunicationData: jasmine.createSpy().and.returnValue(of([getDefaultCommunicationData()])),
      deleteCommunicationData: jasmine.createSpy().and.returnValue(of({}))
    };
    mockCertificateService = {
      deleteFolder: jasmine.createSpy().and.returnValue(of({}))
    };
    mockNotificationService = {
      showMessage: jasmine.createSpy()
    };
    mockContractsService = {
      getAllContract: jasmine.createSpy().and.returnValue(of([]))
    };

    await TestBed.configureTestingModule({
      declarations: [CommunicationPointComponent],
      imports: [TableModule],
      providers: [
        { provide: CommunicationDataService, useValue: mockCommunicationDataService },
        { provide: CertificateService, useValue: mockCertificateService },
        { provide: NotificationService, useValue: mockNotificationService },
        { provide: ContractsService, useValue: mockContractsService }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(CommunicationPointComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call getAllCommunicationData and getAllContract on ngOnInit', () => {
    spyOn(component, 'getAllCommunicationData');
    spyOn(component, 'getAllContract');
    component.ngOnInit();
    expect(component.getAllCommunicationData).toHaveBeenCalled();
    expect(component.getAllContract).toHaveBeenCalled();
  });

  it('should call getAllCommunicationData on onGetAllCommunicationData', () => {
    spyOn(component, 'getAllCommunicationData');
    component.onGetAllCommunicationData();
    expect(component.getAllCommunicationData).toHaveBeenCalled();
  });

  it('should fetch contracts and populate contractNameSet on getAllContract success', () => {
    const contracts = [{
      name: 'Contract1',
      inputIDs: [],
      outputID: 1,
      acknowledgement: { isEnabled: false, isEnhanced: false },
      highFidelity: { isEnabled: false, batchCount: '1', timeLimit: '1' },
      output: undefined,   
      input: undefined    
    }];

    mockContractsService.getAllContract.and.returnValue(of(contracts));
    component.getAllContract();
    expect(component.contractData).toEqual(contracts);
    expect(component.contractNameSet.has('Contract1')).toBeTrue();
  });


  it('should show error notification on getAllContract error', () => {
    mockContractsService.getAllContract.and.returnValue(throwError(() => ({ message: 'error' })));
    component.getAllContract();
    expect(mockNotificationService.showMessage).toHaveBeenCalledWith('error', 'Failure', jasmine.stringMatching('Failed to load contract data.'));
  });

  it('should fetch communication data and populate nameSet on getAllCommunicationData success', () => {
    const commData = [{ ...getDefaultCommunicationData(), name: 'TestName' }];
    mockCommunicationDataService.getAllCommunicationData.and.returnValue(of(commData));
    component.getAllCommunicationData();
    expect(component.communicationData).toEqual(commData);
    expect(component.nameSet.has('TestName')).toBeTrue();
  });

  it('should show error notification on getAllCommunicationData error', () => {
    mockCommunicationDataService.getAllCommunicationData.and.returnValue(throwError(() => 'error'));
    component.getAllCommunicationData();
    expect(component.errorMessage).toContain('Failed to load communication data');
    expect(mockNotificationService.showMessage).toHaveBeenCalledWith('error', 'Failure', jasmine.stringMatching('Failed to load communication data'));
  });

  it('should set dialog and edit mode on edit', () => {
    const data = getDefaultCommunicationData();
    component.edit(data);
    expect(component.isDialogOpen).toBeTrue();
    expect(component.isEditMode).toBeTrue();
    expect(component.currentCommunicationData).toBe(data);
  });

  it('should set alert and toDelete on deleteAlertComponent', () => {
    const data = getDefaultCommunicationData();
    component.deleteAlertComponent(data);
    expect(component.isAlertOpen).toBeTrue();
    expect(component.toDelete).toBe(data);
  });

  it('should show error if toDelete id is missing on delete', () => {
    component.toDelete = { ...getDefaultCommunicationData(), id: -1 };
    spyOn(console, 'error');
    component.delete();
    expect(component.errorMessage).toBe('Communication point ID is missing.');
    expect(console.error).toHaveBeenCalledWith('Communication point ID is missing.');
  });

  it('should show info notification if toDelete is referenced in contract', () => {
    component.toDelete = { ...getDefaultCommunicationData(), id: 1, name: 'Test' };
    component.contractData = component.contractData = [{
      name: 'C',
      inputIDs: [1],
      outputID: 2,
      acknowledgement: { isEnabled: false, isEnhanced: false },
      highFidelity: { isEnabled: false, batchCount: '1', timeLimit: '1' },
      output: undefined,
      input: undefined
    }];

    component.delete();
    expect(mockNotificationService.showMessage).toHaveBeenCalledWith(
      'info',
      'Warning',
      jasmine.stringMatching('is referenced in a contract')
    );
  });

  it('should delete communication data and certificate on delete success', () => {
    component.toDelete = { ...getDefaultCommunicationData(), id: 1, name: 'Test' };
    component.contractData = [];
    spyOn(component, 'onGetAllCommunicationData');
    component.delete();
    expect(mockCommunicationDataService.deleteCommunicationData).toHaveBeenCalledWith(1);
    expect(component.deleteSuccess).toBeTrue();
    expect(component.errorMessage).toBe('');
    expect(component.onGetAllCommunicationData).toHaveBeenCalled();
    expect(mockCertificateService.deleteFolder).toHaveBeenCalledWith('Test');
  });

  it('should show error notification on delete error', () => {
    component.toDelete = { ...getDefaultCommunicationData(), id: 1, name: 'Test' };
    component.contractData = [];
    mockCommunicationDataService.deleteCommunicationData.and.returnValue(throwError(() => ({ message: 'delete error' })));
    component.delete();
    expect(component.deleteSuccess).toBeFalse();
    expect(component.errorMessage).toContain('Error deleting communication point');
    expect(mockNotificationService.showMessage).toHaveBeenCalledWith('error', 'Failure', jasmine.stringMatching('Error deleting communication point'));
  });

  it('should show error notification if folder name is missing on delete', () => {
    component.toDelete = { ...getDefaultCommunicationData(), id: 1, name: '' };
    component.contractData = [];
    component.delete();
    expect(mockNotificationService.showMessage).toHaveBeenCalledWith('error', 'Failure', jasmine.stringMatching('Folder name is missing'));
  });

  it('should call certificateService.deleteFolder and handle success in deleteCertificate', () => {
    component.toDelete = { ...getDefaultCommunicationData(), name: 'Test', certificateDetails: { rootCertificatePath: 'a', clientCertificatePath: 'b' } };
    component['deleteCertificate']();
    expect(mockCertificateService.deleteFolder).toHaveBeenCalledWith('Test');
  });

  it('should open dialog and reset currentCommunicationData on openDialog', () => {
    component.openDialog();
    expect(component.isDialogOpen).toBeTrue();
    expect(component.currentCommunicationData).toEqual(getDefaultCommunicationData());
  });

  it('should close dialog and reset edit mode on closeDialog', () => {
    component.isEditMode = true;
    component.closeDialog(false);
    expect(component.isEditMode).toBeFalse();
    expect(component.isDialogOpen).toBeFalse();
  });

  it('should call delete and close alert on closeAlert with isDeleted true', () => {
    spyOn(component, 'delete');
    component.isAlertOpen = true;
    component.closeAlert(true);
    expect(component.delete).toHaveBeenCalled();
    expect(component.isAlertOpen).toBeFalse();
  });

  it('should only close alert on closeAlert with isDeleted false', () => {
    spyOn(component, 'delete');
    component.isAlertOpen = true;
    component.closeAlert(false);
    expect(component.delete).not.toHaveBeenCalled();
    expect(component.isAlertOpen).toBeFalse();
  });
});
