import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NodeService } from './node.service';
import { ServiceConfiguration } from '../models/service-configuration';
import { ServiceNode } from '../models/service-node';

describe('NodeService', () => {
  let service: NodeService;
  let httpMock: HttpTestingController;
  const apiUrl = 'api/ServiceNode';

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [NodeService]
    });
    service = TestBed.inject(NodeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('#getServiceNodes', () => {
    it('should return a ServiceConfiguration via GET', () => {
      const mockConfig: ServiceConfiguration = {
        tcp: {} as ServiceNode,
        http: {} as ServiceNode,
        webSocketClient: {} as ServiceNode,
        adt: {} as ServiceNode
      };

      service.getServiceNodes().subscribe(config => {
        expect(config).toEqual(mockConfig);
      });

      const req = httpMock.expectOne(apiUrl);
      expect(req.request.method).toBe('GET');
      req.flush(mockConfig);
    });
  });

  const testUpdateNode = (
    methodName: 'updateTCPNode' | 'updateHTTPServerNode' | 'updateWebSocketNode' | 'updateADTNode',
    endpoint: string
  ) => {
    it(`should handle #${methodName} via POST to /${endpoint}`, () => {
      const mockNode: Partial<ServiceNode> = { endPoint: 'test', enableSSL: true, isEnabled: true };
      const mockResponse = { success: true };

      (service[methodName] as (node: ServiceNode) => any)(mockNode as ServiceNode).subscribe((response: any) => {
        expect(response).toEqual(mockResponse);
      });


      const req = httpMock.expectOne(`${apiUrl}/${endpoint}`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(mockNode);
      req.flush(mockResponse);
    });
  };

  describe('Update Node Methods', () => {
    testUpdateNode('updateTCPNode', 'tcp');
    testUpdateNode('updateHTTPServerNode', 'http');
    testUpdateNode('updateWebSocketNode', 'websocket');
    testUpdateNode('updateADTNode', 'adt');
  });
});
