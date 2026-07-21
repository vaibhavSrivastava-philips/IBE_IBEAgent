import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { CommunicationDataService } from './communication-data.service';
import { CommunicationData } from '../models/CommunicationData';

describe('CommunicationDataService', () => {
  let service: CommunicationDataService;
  let httpMock: HttpTestingController;

  const mockDataObject: CommunicationData = {
    id: 1,
    isSSLEnabled: false,
    mode: 'input',
    name: 'Input1',
    type: 'http',
    tcpConfiguration: { ipAddress: '127.0.0.1', port: 8080 },
    httpConfiguration : { endPoint: '/data' },
    webSocketConfiguration : { endPoint: '/ws' },
    certificateDetails: {
      rootCertificatePath: '',
      clientCertificatePath: '',
      clientCertificatePassword: '',
    },
    proxyConfigurations: {
      isEnabled: false,
      proxyAddress: '',
      proxyPort: '',
      proxyUsername: '',
      proxyPassword: '',
    },
    connectionRetry: {
      retryAttempts: 3,
      baseRetryDelayInSeconds: 1
    },
    messageRetry: {
      retryAttempts: 3,
      baseRetryDelayInSeconds: 1
    },
    s3Configuration: {
      serviceId: '',
      tenantName: '',
      collectorId: '',
      institutionName: '',
      gatewayUrl: '',
      iamHost: '',
      timeZone: '',
      privateKeyPath: '',
      privateKeyPassword: ''
    },
    cacheConfiguration: {
      cacheReconciliationEndPoint: '',
      cacheRelaodEndPoint: '',
      cacheCertificatePath: '',
      cacheCertificatePassword: ''
    }
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [CommunicationDataService]
    });
    service = TestBed.inject(CommunicationDataService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getCommunicationDataById', () => {
    it('should return an Observable<CommunicationData>', () => {
      service.getCommunicationDataById(1).subscribe(data => {
        expect(data).toEqual(mockDataObject);
      });

      const req = httpMock.expectOne('/api/CommunicationPoint/1');
      expect(req.request.method).toBe('GET');
      req.flush(mockDataObject);
    });
  });

  describe('getAllCommunicationData', () => {
    it('should return an Observable<CommunicationData[]>', () => {
      const mockDataArray: CommunicationData[] = [mockDataObject, { ...mockDataObject, id: 2, name: 'Input2' }];

      service.getAllCommunicationData().subscribe(data => {
        expect(data.length).toBe(2);
        expect(data).toEqual(mockDataArray);
      });

      const req = httpMock.expectOne('/api/CommunicationPoint');
      expect(req.request.method).toBe('GET');
      req.flush(mockDataArray);
    });
  });

  describe('addCommunicationData', () => {
    it('should POST data and return a success message', () => {
      const newData: CommunicationData = { ...mockDataObject, id: 3, name: 'Input3' };
      const mockResponse = 'Data added successfully';

      service.addCommunicationData(newData).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne('/api/CommunicationPoint');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(newData);
      req.flush(mockResponse, { status: 201, statusText: 'Created' });
    });
  });

  describe('updateCommunicationData', () => {
    it('should PUT to update data and return a success message', () => {
      const updatedData: CommunicationData = { ...mockDataObject, name: 'Input1 Updated' };
      const dataId = 1;
      const mockResponse = 'Data updated successfully';

      service.updateCommunicationData(dataId, updatedData).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`/api/CommunicationPoint/${dataId}`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(updatedData);
      req.flush(mockResponse, { status: 200, statusText: 'OK' });
    });
  });

  describe('deleteCommunicationData', () => {
    it('should DELETE data and return a success message', () => {
      const dataId = 1;
      const mockResponse = 'Data deleted successfully';

      service.deleteCommunicationData(dataId).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`/api/CommunicationPoint/${dataId}`);
      expect(req.request.method).toBe('DELETE');
      req.flush(mockResponse, { status: 200, statusText: 'OK' });
    });
  });
});
