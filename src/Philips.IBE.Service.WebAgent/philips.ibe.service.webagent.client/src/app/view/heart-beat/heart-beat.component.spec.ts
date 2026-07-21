import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HeartBeatComponent } from './heart-beat.component';
import { CommunicationDataService } from '../../services/communication-data.service';
import { HeartBeatService } from '../../services/heartbeat.service';
import { ChangeDetectorRef } from '@angular/core';
import { of, throwError } from 'rxjs';

describe('HeartBeatComponent', () => {
  let component: HeartBeatComponent;
  let fixture: ComponentFixture<HeartBeatComponent>;
  let communicationDataServiceSpy: jasmine.SpyObj<CommunicationDataService>;
  let heartBeatServiceSpy: jasmine.SpyObj<HeartBeatService>;
  let cdrSpy: jasmine.SpyObj<ChangeDetectorRef>;

  const mockData = [
    { id: 1, name: 'Comm1', mode: 'server', type: 'tcp', ipAddress: '127.0.0.1', port: '8080', status: false },
    { id: 2, name: 'Comm2', mode: 'client', type: 'tcp', ipAddress: '127.0.0.2', port: '8081', status: false }
  ];


  beforeEach(async () => {
    communicationDataServiceSpy = jasmine.createSpyObj('CommunicationDataService', ['getAllCommunicationData']);
    heartBeatServiceSpy = jasmine.createSpyObj('HeartBeatService', ['checkServerPortOpen', 'getTCPPortList']);
    cdrSpy = jasmine.createSpyObj('ChangeDetectorRef', ['detectChanges']);

    await TestBed.configureTestingModule({
      declarations: [HeartBeatComponent],
      providers: [
        { provide: CommunicationDataService, useValue: communicationDataServiceSpy },
        { provide: HeartBeatService, useValue: heartBeatServiceSpy },
        { provide: ChangeDetectorRef, useValue: cdrSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(HeartBeatComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call getAllCommunicationData and start periodic status check on init', () => {
    spyOn(component, 'getAllCommunicationData');
    spyOn(component, 'startPeriodicStatusCheck');
    component.ngOnInit();
    expect(component.getAllCommunicationData).toHaveBeenCalled();
    expect(component.startPeriodicStatusCheck).toHaveBeenCalled();
  });

  it('should unsubscribe on destroy', () => {
    const unsubscribeSpy = jasmine.createSpy('unsubscribe');
    (component as any).statusCheckSubscription = { unsubscribe: unsubscribeSpy };
    component.ngOnDestroy();
    expect(unsubscribeSpy).toHaveBeenCalled();
    expect(component['statusCheckSubscription']).toBeNull();
  });

  it('should handle error in getAllCommunicationData', () => {
    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(throwError(() => new Error('fail')));
    spyOn(console, 'error');
    component.getAllCommunicationData();
    expect(console.error).toHaveBeenCalled();
  });

  it('should filter communication data in filterGlobal', () => {
    component.allCommunicationData = mockData;
    component.filterGlobal('comm1', 'contains');
    expect(component.filteredCommunicationData.length).toBe(1);
    expect(component.filteredCommunicationData[0].name).toBe('Comm1');
  });

  it('should handle error in checkAllStatuses for server', fakeAsync(() => {
    component.allCommunicationData = [mockData[0]];
    heartBeatServiceSpy.checkServerPortOpen.and.returnValue(throwError(() => new Error('fail')));
    component.checkAllStatuses();
    tick();
    expect(component.allCommunicationData[0].status).toBeFalse();
  }));

  it('should handle error in checkAllStatuses for client', fakeAsync(() => {
    component.allCommunicationData = [mockData[1]];
    heartBeatServiceSpy.getTCPPortList.and.returnValue(throwError(() => new Error('fail')));
    component.checkAllStatuses();
    tick();
    expect(component.allCommunicationData[0].status).toBeFalse();
  }));

  it('should refresh all statuses', () => {
    spyOn(component, 'checkAllStatuses');
    component.refreshAllStatuses();
    expect(component.checkAllStatuses).toHaveBeenCalled();
  });

  it('should start and stop periodic status check', fakeAsync(() => {
    spyOn(component, 'checkAllStatuses');
    component.startPeriodicStatusCheck();
    tick(1);
    expect(component.checkAllStatuses).toHaveBeenCalled();
    component.stopPeriodicStatusCheck();
    expect(component['statusCheckSubscription']).toBeNull();
  }));
});
