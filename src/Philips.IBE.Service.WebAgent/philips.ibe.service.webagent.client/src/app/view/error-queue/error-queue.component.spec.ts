import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ErrorQueueComponent } from './error-queue.component';
import { ErrorQueueService } from '../../services/error-queue.service';
import { CommunicationDataService } from '../../services/communication-data.service';
import { NotificationService } from '../../services/notification.service';
import { of, throwError, Subscription } from 'rxjs';
import { ErrorQueue } from '../../models/error-queue';
import { CommunicationData } from '../../models/CommunicationData';
import { getDefaultCommunicationData } from '../../models/CommunicationComponent';
import { TableModule } from 'primeng/table';

function createCommunicationData(overrides: Partial<CommunicationData>): CommunicationData {
  return { ...getDefaultCommunicationData(), ...overrides };
}

describe('ErrorQueueComponent', () => {
  let component: ErrorQueueComponent;
  let fixture: ComponentFixture<ErrorQueueComponent>;
  let errorQueueServiceSpy: jasmine.SpyObj<ErrorQueueService>;
  let communicationDataServiceSpy: jasmine.SpyObj<CommunicationDataService>;
  let notificationServiceSpy: jasmine.SpyObj<NotificationService>;

  const errorQueueBase: ErrorQueue = {
    id: 1,
    message: 'msg',
    senderId: 1,
    timeStamp: '2024-01-01T00:00:00Z'
  };

  beforeEach(async () => {
    errorQueueServiceSpy = jasmine.createSpyObj('ErrorQueueService', ['getErrorQueue', 'UpdateErrorQueue']);
    communicationDataServiceSpy = jasmine.createSpyObj('CommunicationDataService', ['getAllCommunicationData']);
    notificationServiceSpy = jasmine.createSpyObj('NotificationService', ['showMessage']);

    await TestBed.configureTestingModule({
      declarations: [ErrorQueueComponent],
      imports: [TableModule],
      providers: [
        { provide: ErrorQueueService, useValue: errorQueueServiceSpy },
        { provide: CommunicationDataService, useValue: communicationDataServiceSpy },
        { provide: NotificationService, useValue: notificationServiceSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ErrorQueueComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should fetch communication data and error queue on init', () => {
    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(of([]));
    errorQueueServiceSpy.getErrorQueue.and.returnValue(of([]));
    component.ngOnInit();
    expect(communicationDataServiceSpy.getAllCommunicationData).toHaveBeenCalled();
    expect(errorQueueServiceSpy.getErrorQueue).toHaveBeenCalled();
  });

  it('should unsubscribe on destroy', () => {
    spyOn(component.intervalSubscription, 'unsubscribe');
    component.ngOnDestroy();
    expect(component.intervalSubscription.unsubscribe).toHaveBeenCalled();
  });

  it('should set errorQueue on fetchErrorQueue success', () => {
    const mockData: ErrorQueue[] = [errorQueueBase];
    errorQueueServiceSpy.getErrorQueue.and.returnValue(of(mockData));
    component.fetchErrorQueue();
    expect(component.errorQueue).toEqual(mockData);
  });

  it('should show error message on fetchErrorQueue error', () => {
    errorQueueServiceSpy.getErrorQueue.and.returnValue(throwError(() => 'err'));
    component.fetchErrorQueue();
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'Error Fetching Error Queue', 'err');
  });

  it('should call UpdateErrorQueue and refresh on updateErrorQueue', () => {
    const item: ErrorQueue = { ...errorQueueBase, id: 2 };
    errorQueueServiceSpy.UpdateErrorQueue.and.returnValue(of({}));
    spyOn(component, 'fetchErrorQueue');
    component.updateErrorQueue(item);
    expect(errorQueueServiceSpy.UpdateErrorQueue).toHaveBeenCalledWith(2);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('success', 'Error Queue Updated', '');
    expect(component.fetchErrorQueue).toHaveBeenCalled();
  });

  it('should show error message on updateErrorQueue error', () => {
    const item: ErrorQueue = { ...errorQueueBase, id: 4 };
    errorQueueServiceSpy.UpdateErrorQueue.and.returnValue(throwError(() => 'err'));
    component.updateErrorQueue(item);
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'Error Queue Update Failed', 'err');
  });

  it('should set communicationData on getAllCommunicationData success', () => {
    const commData: CommunicationData[] = [
      createCommunicationData({ id: 1, name: 'Test', mode: 'test', type: 'tcp', tcpConfiguration: { ipAddress: '127.0.0.1', port: 1234 } }),
    ];
    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(of(commData));
    component.getAllCommunicationData();
    expect(component.communicationData).toEqual(commData);
  });

  it('should show error message on getAllCommunicationData error', () => {
    communicationDataServiceSpy.getAllCommunicationData.and.returnValue(throwError(() => 'err'));
    component.getAllCommunicationData();
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('error', 'Error Fetching Communication Data', 'err');
  });

  it('should show message details', () => {
    const item: ErrorQueue = { ...errorQueueBase, message: 'test message' };
    component.showErrorDetails(item);
    expect(component.visible).toBeTrue();
    expect(component.dialogTitle).toBe('Message Details');
    expect(component.dialogMessage).toBe('test message');
  });

  it('should get communication data name by id', () => {
    component.communicationData = [{ id: 1, name: 'Comm1' } as any];
    expect(component.getCommunicationDataById(1)).toBe('Comm1');
    expect(component.getCommunicationDataById(2)).toBe('');
  });
});
