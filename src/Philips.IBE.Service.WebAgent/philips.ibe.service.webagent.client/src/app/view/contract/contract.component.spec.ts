import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ContractComponent } from './contract.component';
import { ContractsService } from '../../services/contracts.service';
import { CommunicationDataService } from '../../services/communication-data.service';
import { NotificationService } from '../../services/notification.service';
import { of, throwError } from 'rxjs';
import { Contract } from '../../models/Contract';
import { CommunicationData } from '../../models/CommunicationData';
import { getDefaultCommunicationData } from '../../models/CommunicationComponent';

function createCommunicationData(overrides: Partial<CommunicationData>): CommunicationData {
  return { ...getDefaultCommunicationData(), ...overrides };
}

describe('ContractComponent', () => {
  let component: ContractComponent;
  let fixture: ComponentFixture<ContractComponent>;
  let contractsServiceSpy: jasmine.SpyObj<ContractsService>;
  let communicationDataServiceSpy: jasmine.SpyObj<CommunicationDataService>;
  let notificationServiceSpy: jasmine.SpyObj<NotificationService>;

  beforeEach(async () => {
    contractsServiceSpy = jasmine.createSpyObj('ContractsService', ['getAllContract', 'deleteContract']);
    communicationDataServiceSpy = jasmine.createSpyObj('CommunicationDataService', ['getAllCommunicationData']);
    notificationServiceSpy = jasmine.createSpyObj('NotificationService', ['showMessage']);

    await TestBed.configureTestingModule({
      declarations: [ContractComponent],
      providers: [
        { provide: ContractsService, useValue: contractsServiceSpy },
        { provide: CommunicationDataService, useValue: communicationDataServiceSpy },
        { provide: NotificationService, useValue: notificationServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ContractComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call getAllCommunicationData and getAllContract on ngOnInit', () => {
    const commData: CommunicationData[] = [
      createCommunicationData({ id: 1, name: 'Test', mode: 'test', type: 'tcp', tcpConfiguration: { ipAddress: '127.0.0.1', port: 1234 } }),
    ];


    const contractData: Contract[] = [{ ...component.newContractPoint, name: 'C1' }];
    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(of(commData));
    contractsServiceSpy.getAllContract.and.returnValue(of(contractData));

    component.ngOnInit();

    expect(communicationDataServiceSpy.getAllCommunicationData).toHaveBeenCalled();
    expect(contractsServiceSpy.getAllContract).toHaveBeenCalled();
    expect(component.communicationData).toEqual(commData);
    expect(component.contractData).toEqual(contractData);
  });

  it('should handle error in getAllCommunicationData', () => {
    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(throwError(() => new Error('fail')));
    contractsServiceSpy.getAllContract.and.returnValue(of([]));
    component.ngOnInit();
    expect(component.errorMessage).toContain('Failed to load communication data');
    expect(notificationServiceSpy.showMessage).toHaveBeenCalled();
  });

  it('should fetch communication data name by id', () => {
    component.communicationData = [
      createCommunicationData({ id: 2, name: 'Comm2' }),
    ];
    expect(component.fetchCommunicationData(2)).toBe('Comm2');
    expect(component.fetchCommunicationData(-1)).toBe('');
  });

  it('should call getAllContract and update contractNameSet', () => {
    const contracts: Contract[] = [
      { ...component.newContractPoint, name: 'A' },
      { ...component.newContractPoint, name: 'B' }
    ];
    contractsServiceSpy.getAllContract.and.returnValue(of(contracts));
    component.getAllContract();
    expect(component.contractData).toEqual(contracts);
    expect(component.contractNameSet.has('A')).toBeTrue();
    expect(component.contractNameSet.has('B')).toBeTrue();
  });

  it('should handle error in getAllContract', () => {
    contractsServiceSpy.getAllContract.and.returnValue(throwError(() => ({ message: 'fail' })));
    component.getAllContract();
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'Failure', jasmine.stringMatching('Failed to load contract data.'));
  });

  it('should open and close contract dialog', () => {
    component.isEditMode = true;
    component.isContractDialogOpen = false;
    component.openContractDialog();
    expect(component.isEditMode).toBeFalse();
    expect(component.isContractDialogOpen).toBeTrue();

    component.closeContractDialog(false);
    expect(component.isContractDialogOpen).toBeFalse();
  });

  it('should reset newContractPoint', () => {
    component.newContractPoint.name = 'changed';
    component.resetNewContractPoint();
    expect(component.newContractPoint.name).toBe('');
    expect(component.newContractPoint.acknowledgement.isEnabled).toBeFalse();
  });

  it('should set edit mode and open dialog on edit', () => {
    const contract: Contract = { ...component.newContractPoint, name: 'EditMe' };
    component.edit(contract);
    expect(component.isEditMode).toBeTrue();
    expect(component.editData).toBe(contract);
    expect(component.isContractDialogOpen).toBeTrue();
  });

  it('should set dialog message and open alert on deleteAlertComponent', () => {
    const contract: Contract = { ...component.newContractPoint, name: 'DelMe' };
    component.deleteAlertComponent(contract);
    expect(component.dialogMessage).toContain('DelMe');
    expect(component.isAlertOpen).toBeTrue();
    expect(component.toDelete).toBe(contract);
  });

  it('should call deleteContract and refresh contracts on delete', fakeAsync(() => {
    contractsServiceSpy.deleteContract.and.returnValue(of({}));
    spyOn(component, 'onGetAllContract');
    component.toDelete = { ...component.newContractPoint, name: 'DelMe' };
    component.delete();
    tick();
    expect(component.deleteSuccess).toBeTrue();
    expect(component.errorMessage).toBe('');
    expect(component.onGetAllContract).toHaveBeenCalled();
  }));

  it('should handle error on delete', fakeAsync(() => {
    contractsServiceSpy.deleteContract.and.returnValue(throwError(() => ({ message: 'fail' })));
    component.toDelete = { ...component.newContractPoint, name: 'DelMe' };
    component.delete();
    tick();
    expect(component.deleteSuccess).toBeFalse();
    expect(component.errorMessage).toContain('Error deleting contract point');
  }));

  it('should call delete on closeAlert if isDeleted is true', () => {
    spyOn(component, 'delete');
    component.isAlertOpen = true;
    component.closeAlert(true);
    expect(component.delete).toHaveBeenCalled();
    expect(component.isAlertOpen).toBeFalse();
  });

  it('should just close alert if isDeleted is false', () => {
    spyOn(component, 'delete');
    component.isAlertOpen = true;
    component.closeAlert(false);
    expect(component.delete).not.toHaveBeenCalled();
    expect(component.isAlertOpen).toBeFalse();
  });

  it('should close contract dialog on escape key if dialog is open', () => {
    component.isDialogOpen = true;
    spyOn(component, 'closeContractDialog');
    component.handleEscapeKey();
    expect(component.closeContractDialog).toHaveBeenCalledWith(false);
  });

  it('should close contract dialog on overlay click', () => {
    component.isDialogOpen = true;
    spyOn(component, 'closeContractDialog');
    const div = document.createElement('div');
    div.classList.add('dialog-overlay');
    const event = new MouseEvent('click');
    Object.defineProperty(event, 'target', { writable: false, value: div });
    component.onOverlayClick(event);
    expect(component.closeContractDialog).toHaveBeenCalledWith(false);
  });
});
