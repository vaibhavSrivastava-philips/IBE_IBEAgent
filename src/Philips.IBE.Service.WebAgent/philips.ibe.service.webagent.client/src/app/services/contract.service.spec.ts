import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ContractsService } from './contracts.service';
import { Contract } from '../models/Contract';
import { Acknowledgement } from '../models/Acknowledgement';
import { HighFidelity } from '../models/HighFidelity';


describe('ContractsService', () => {
  let service: ContractsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [ContractsService]
    });
    service = TestBed.inject(ContractsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify(); 
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getAllContract', () => {
    it('should return an Observable<Contract[]>', () => {
      const mockContracts: Contract[] = [
        {
          name: 'Contract A',
          acknowledgement: {} as Acknowledgement,
          highFidelity: {} as HighFidelity,
          inputIDs: [1, 2],
          outputID: 3,
        },
        {
          name: 'Contract B',
          acknowledgement: {} as Acknowledgement,
          highFidelity: {} as HighFidelity,
          inputIDs: [4, 5],
          outputID: 6,
        }
      ];

      service.getAllContract().subscribe(contracts => {
        expect(contracts.length).toBe(2);
        expect(contracts).toEqual(mockContracts);
      });

      const req = httpMock.expectOne('/api/Contract');
      expect(req.request.method).toBe('GET');
      req.flush(mockContracts);
    });
  });

  describe('addContract', () => {
    it('should POST a contract and return a success message', () => {
      const newContract: Contract = {
        name: 'Contract C',
        acknowledgement: {} as Acknowledgement,
        highFidelity: {} as HighFidelity,
        inputIDs: [7, 8],
        outputID: 9,
      };
      const mockResponse = 'Contract added successfully';

      service.addContract(newContract).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne('/api/Contract');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(newContract);
      req.flush(mockResponse, { status: 200, statusText: 'OK' });
    });
  });

  describe('updateContract', () => {
    it('should PUT to update a contract and return a success message', () => {
      const updatedContract: Contract = {
        name: 'Contract A Updated',
        acknowledgement: {} as Acknowledgement,
        highFidelity: {} as HighFidelity,
        inputIDs: [1, 2],
        outputID: 3,
      };
      const oldName = 'Contract A';
      const mockResponse = 'Contract updated successfully';

      service.updateContract(oldName, updatedContract).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`/api/Contract/${oldName}`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(updatedContract);
      req.flush(mockResponse, { status: 200, statusText: 'OK' });
    });
  });

  describe('deleteContract', () => {
    it('should DELETE a contract and return a success message', () => {
      const contractName = 'Contract B';
      const mockResponse = 'Contract deleted successfully';

      service.deleteContract(contractName).subscribe(response => {
        expect(response).toEqual(mockResponse);
      });

      const req = httpMock.expectOne(`/api/Contract/${contractName}`);
      expect(req.request.method).toBe('DELETE');
      req.flush(mockResponse, { status: 200, statusText: 'OK' });
    });
  });
});
