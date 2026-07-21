import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { HeartBeatService } from './heartbeat.service';
import { HeartBeat } from '../models/HeartBeat';

describe('HeartBeatService', () => {
  let service: HeartBeatService;
  let httpMock: HttpTestingController;

 beforeEach(() => {
   TestBed.configureTestingModule({
     imports: [HttpClientTestingModule],
      providers: [HeartBeatService]
    });
    service = TestBed.inject(HeartBeatService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

 describe('getTCPPortList', () => {
    it('should return an Observable<Array<string>>', () => {
      const mockPorts = ['80', '443', '8080'];

      service.getTCPPortList().subscribe(ports => {
        expect(ports.length).toBe(3);
        expect(ports).toEqual(mockPorts);
      });

      const req = httpMock.expectOne('/api/HeartBeat/client');
      expect(req.request.method).toBe('GET');
      req.flush(mockPorts);
    });
  });

  describe('checkServerPortOpen', () => {
    it('should handle host with http protocol', () => {
      const mockHeartBeat: any = { isPortOpen: true, message: 'Port is open' };
      const host = 'http://example.com:8080';
      const port = 8080; 

      service.checkServerPortOpen(host, port).subscribe(response => {
        expect(response).toEqual(mockHeartBeat);
      });

      const req = httpMock.expectOne('/api/HeartBeat/server?host=example.com&port=8080');
      expect(req.request.method).toBe('GET');
      req.flush(mockHeartBeat);
    });

    it('should handle host with https protocol', () => {
      const mockHeartBeat: any = { isPortOpen: true, message: 'Port is open' };
      const host = 'https://example.com:443';
      const port = 443;

      service.checkServerPortOpen(host, port).subscribe(response => {
        expect(response).toEqual(mockHeartBeat);
      });

      const req = httpMock.expectOne('/api/HeartBeat/server?host=example.com&port=443');
      expect(req.request.method).toBe('GET');
      req.flush(mockHeartBeat);
    });

    it('should handle host without protocol', () => {
      const mockHeartBeat: any = { isPortOpen: false, message: 'Port is closed' };
      const host = 'localhost';
      const port = 3000;

      service.checkServerPortOpen(host, port).subscribe(response => {
        expect(response).toEqual(mockHeartBeat);
      });

      const req = httpMock.expectOne(`/api/HeartBeat/server?host=${host}&port=${port}`);
      expect(req.request.method).toBe('GET');
      req.flush(mockHeartBeat);
    });
  });
});
