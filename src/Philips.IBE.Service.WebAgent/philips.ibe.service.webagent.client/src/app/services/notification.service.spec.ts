import { TestBed } from '@angular/core/testing';
import { ToastService } from '@filament/angular';
import { NotificationService } from './notification.service';

class MockToastService {
  open(options: any) { }
  close(id: string) { }
}

describe('NotificationService', () => {
  let service: NotificationService;
  let toastService: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        NotificationService,
        { provide: ToastService, useClass: MockToastService }
      ]
    });
    service = TestBed.inject(NotificationService);
    toastService = TestBed.inject(ToastService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('#showMessage', () => {
    it('should call toastService.open with correct title, content and signal', () => {
      const openSpy = spyOn(toastService, 'open');
      const testSeverity = 'success';
      const testTitle = 'Success';
      const testBody = 'Operation completed successfully.';

      service.showMessage(testSeverity, testTitle, testBody);

      expect(openSpy).toHaveBeenCalledTimes(1);

      const arg = openSpy.calls.mostRecent().args[0];
      expect(arg.title).toBe(testTitle);
      expect(arg.content).toBe(testBody);
      expect(arg.signal).toBe('success');
      expect(arg.showCloseButton).toBeTrue();
      expect(arg.timeout).toBe(5000);
    });

    it('should map error severity to error signal', () => {
      const openSpy = spyOn(toastService, 'open');

      service.showMessage('error', 'Error', 'Something went wrong.');

      const arg = openSpy.calls.mostRecent().args[0];
      expect(arg.signal).toBe('error');
    });

    it('should map unknown severity to default signal', () => {
      const openSpy = spyOn(toastService, 'open');

      service.showMessage('unknown', 'Notice', 'Some message.');

      const arg = openSpy.calls.mostRecent().args[0];
      expect(arg.signal).toBe('default');
    });
  });
});
