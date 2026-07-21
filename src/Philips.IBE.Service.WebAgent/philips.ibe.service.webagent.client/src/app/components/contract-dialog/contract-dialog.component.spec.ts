import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ContractDialogComponent } from './contract-dialog.component';
import { ContractsService } from '../../services/contracts.service';
import { CommunicationDataService } from '../../services/communication-data.service';
import { CUSTOM_ELEMENTS_SCHEMA, Component, Input, forwardRef } from '@angular/core';
import { of, throwError } from 'rxjs';
import { FormsModule, ReactiveFormsModule, NG_VALUE_ACCESSOR, ControlValueAccessor } from '@angular/forms';
import { CommunicationData } from '../../models/CommunicationData';
import { getDefaultCommunicationData } from '../../models/CommunicationComponent';

function createCommunicationData(overrides: Partial<CommunicationData>): CommunicationData {
  return { ...getDefaultCommunicationData(), ...overrides };
}

@Component({
  selector: 'dls-multiselect',
  template: '',
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => DlsMultiselectStubComponent),
    multi: true
  }]
})
class DlsMultiselectStubComponent implements ControlValueAccessor {
  @Input() options: any;
  writeValue(obj: any): void { }
  registerOnChange(fn: any): void { }
  registerOnTouched(fn: any): void { }
  setDisabledState?(isDisabled: boolean): void { }
}

@Component({
  selector: 'dls-toggle-switch',
  template: '',
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => DlsToggleSwitchStubComponent),
    multi: true
  }]
})
class DlsToggleSwitchStubComponent implements ControlValueAccessor {
  @Input() disabled: boolean = false;
  writeValue(obj: any): void { }
  registerOnChange(fn: any): void { }
  registerOnTouched(fn: any): void { }
  setDisabledState?(isDisabled: boolean): void { }
}




describe('ContractDialogComponent', () => {
  let component: ContractDialogComponent;
  let fixture: ComponentFixture<ContractDialogComponent>;
  let contractsServiceSpy: jasmine.SpyObj<ContractsService>;
  let communicationDataServiceSpy: jasmine.SpyObj<CommunicationDataService>;

  beforeEach(async () => {
    contractsServiceSpy = jasmine.createSpyObj('ContractsService', ['addContract', 'updateContract']);
    communicationDataServiceSpy = jasmine.createSpyObj('CommunicationDataService', ['getAllCommunicationData']);

    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      declarations: [
        ContractDialogComponent,
        DlsMultiselectStubComponent,      
        DlsToggleSwitchStubComponent   
      ],
      imports: [FormsModule, ReactiveFormsModule],
      providers: [
        { provide: ContractsService, useValue: contractsServiceSpy },
        { provide: CommunicationDataService, useValue: communicationDataServiceSpy },
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();

    fixture = TestBed.createComponent(ContractDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call getAllCommunicationData on ngOnInit', () => {
    spyOn(component, 'getAllCommunicationData');
    component.ngOnInit();
    expect(component.getAllCommunicationData).toHaveBeenCalled();
  });

  it('should reset contract if not in edit mode on ngOnInit', () => {
    component.isEditMode = false;
    component.currentContract.name = 'test';
    component.ngOnInit();
    expect(component.currentContract.name).toBe('');
  });

  it('should not reset contract if in edit mode on ngOnInit', () => {
    component.isEditMode = true;
    component.currentContract.name = 'test';
    component.ngOnInit();
    expect(component.currentContract.name).toBe('test');
  });

  it('should set errorMessage if contract name is empty on validateContract', () => {
    component.currentContract.name = '';
    component.currentContract.inputIDs = [1];
    expect(component['validateContract']()).toBeFalse();
    expect(component.errorMessage).toContain('Contract name is required');
  });

  it('should set errorMessage if contract name is not unique on validateContract', () => {
    component.contractNameSet = new Set(['existing']);
    component.currentContract.name = 'existing';
    component.currentContract.inputIDs = [1];
    expect(component['validateContract']()).toBeFalse();
    expect(component.errorMessage).toContain('Contract names must be unique');
  });

  it('should set errorMessage if no inputIDs on validateContract', () => {
    component.currentContract.name = 'unique';
    component.currentContract.inputIDs = [];
    expect(component['validateContract']()).toBeFalse();
    expect(component.errorMessage).toContain('Each contract must have at least one input');
  });

  it('should return true for valid contract on validateContract', () => {
    component.contractNameSet = new Set(['other']);
    component.currentContract.name = 'unique';
    component.currentContract.inputIDs = [1];
    expect(component['validateContract']()).toBeTrue();
  });

  it('should emit getAllContract and close dialog on successful addNewContractData', fakeAsync(() => {
    spyOn(component.getAllContract, 'emit');
    spyOn(component, 'closeDialog');
    contractsServiceSpy.addContract.and.returnValue(of('success'));
    (component as any).addNewContractData();
    tick();
    expect(component.successMessage).toContain('added successfully');
    expect(component.getAllContract.emit).toHaveBeenCalled();
    expect(component.closeDialog).toHaveBeenCalled();
  }));

  it('should set errorMessage on addNewContractData error', fakeAsync(() => {
    contractsServiceSpy.addContract.and.returnValue(throwError(() => new Error('fail')));
    (component as any).addNewContractData();
    tick();
    expect(component.errorMessage).toContain('Failed to add contract');
  }));

  it('should emit getAllContract and close dialog on successful updateExistingContractData', fakeAsync(() => {
    spyOn(component.getAllContract, 'emit');
    spyOn(component, 'closeDialog');
    contractsServiceSpy.updateContract.and.returnValue(of({}));
    component.currentContract.name = 'unique';
    (component as any).updateExistingContractData();
    tick();
    expect(component.successMessage).toContain('updated successfully');
    expect(component.getAllContract.emit).toHaveBeenCalled();
    expect(component.closeDialog).toHaveBeenCalled();
  }));

  it('should set errorMessage on updateExistingContractData error', fakeAsync(() => {
    contractsServiceSpy.updateContract.and.returnValue(throwError(() => new Error('fail')));
    component.currentContract.name = 'unique';
    (component as any).updateExistingContractData();
    tick();
    expect(component.errorMessage).toContain('Failed to update contract');
  }));

  it('should emit close event on closeDialog', () => {
    spyOn(component.close, 'emit');
    component.closeDialog();
    expect(component.close.emit).toHaveBeenCalledWith(false);
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

  it('should call getAllCommunicationData and set communicationData', fakeAsync(() => {
    const data = [
      createCommunicationData({ id: 1, mode: 'input', name: 'Input1', type: 'http' }),
      createCommunicationData({ id: 2, mode: 'output', name: 'Output1', type: 'websocket' }),
      createCommunicationData({ id: 3, mode: 'output', name: 'Output2', type: 'http' }),
    ];

    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(of(data));
    component.getAllCommunicationData();
    tick();
    expect(component.communicationData.length).toBe(3);
    expect(component.inputCommunicationData.length).toBeGreaterThan(0);
    expect(component.outputCommunicationData.length).toBeGreaterThan(0);
  }));


  it('should set errorMessage on getAllCommunicationData error', fakeAsync(() => {
    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(throwError(() => new Error('fail')));
    component.getAllCommunicationData();
    tick();
    expect(component.errorMessage).toContain('Failed to load communication data');
  }));

  it('should return index from trackByFn', () => {
    expect(component.trackByFn(1, {})).toBe(1);
  });
});
