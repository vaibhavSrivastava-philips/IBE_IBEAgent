import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { CertificateService } from './certificate.service';

describe('CertificateService', () => {
  let service: CertificateService;
  let httpMock: HttpTestingController;
  const apiUrl = 'api/certificate';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [CertificateService]
    });
    service = TestBed.inject(CertificateService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('#upload2Files', () => {
    it('should upload two files via POST request', () => {
      const mockFile1 = new File([''], 'file1.txt');
      const mockFile2 = new File([''], 'file2.txt');
      const folderName = 'test-folder';
      const mockResponse = { success: true };

      service.upload2Files(mockFile1, mockFile2, folderName).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`${apiUrl}/multiple?folderName=${folderName}`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.get('file1')).toEqual(mockFile1);
      expect(req.request.body.get('file2')).toEqual(mockFile2);

      req.flush(mockResponse);
    });
  });

  describe('#uploadFile', () => {
    it('should upload a single file via POST request', () => {
      const mockFile = new File([''], 'single.txt');
      const folderName = 'single-folder';
      const mockResponse = { success: true };

      service.uploadFile(mockFile, folderName).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`${apiUrl}/single?folderName=${folderName}`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.get('file1')).toEqual(mockFile);

      req.flush(mockResponse);
    });
  });

  describe('#deleteFolder', () => {
    it('should delete a folder via DELETE request', () => {
      const folderName = 'folder-to-delete';
      const mockResponse = { success: true };

      service.deleteFolder(folderName).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`${apiUrl}/folder?folderName=${folderName}`);
      expect(req.request.method).toBe('DELETE');

      req.flush(mockResponse);
    });
  });

  describe('#deleteFile', () => {
    it('should delete a specific file via DELETE request', () => {
      const folderName = 'my-folder';
      const fileName = 'file-to-delete.txt';
      const mockResponse = { success: true };

      service.deleteFile(folderName, fileName).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`${apiUrl}/file?folderName=${folderName}&fileName=${fileName}`);
      expect(req.request.method).toBe('DELETE');

      req.flush(mockResponse);
    });
  });
});
