import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ErrorQueueService } from './error-queue.service';
import { ErrorQueue } from '../models/error-queue';

describe('ErrorQueueService', () => {
  let service: ErrorQueueService;
  let httpMock: HttpTestingController;
  const apiUrl = 'api/errorQueue';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ErrorQueueService]
    });
    service = TestBed.inject(ErrorQueueService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify(); 
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('#getErrorQueue', () => {
    it('should return an Observable<ErrorQueue[]>', () => {
      const mockErrorQueue: ErrorQueue[] = [
        {
          id: 1,
          message: 'Test Message 1',
          senderId: 1,
          timeStamp: '2024-01-01T00:00:00Z'
        },
        {
          id: 2,
          message: 'Test Message 2',
          senderId: 2,
          timeStamp: '2024-01-03T00:00:00Z'
        }
      ];


      service.getErrorQueue().subscribe(errors => {
        expect(errors.length).toBe(2);
        expect(errors).toEqual(mockErrorQueue);
      });

      const req = httpMock.expectOne(apiUrl);
      expect(req.request.method).toBe('GET');
      req.flush(mockErrorQueue);
    });
  });

  describe('#UpdateErrorQueue', () => {
    it('should send a PUT request to update an error queue item', () => {
      const testId = 123;
      const mockResponse = { status: 'updated' };

      service.UpdateErrorQueue(testId).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`${apiUrl}/${testId}`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toBeNull(); 
      req.flush(mockResponse);
    });
  });
});
