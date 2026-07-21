import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { ServiceConfiguration } from '../models/service-configuration';
import { ServiceNode } from '../models/service-node';

@Injectable({
  providedIn: 'root'
})
export class NodeService {
  apiUrl = 'api/ServiceNode';
  constructor(private http: HttpClient) { }

  getServiceNodes() {
    return this.http.get<ServiceConfiguration>(this.apiUrl);
  }

  updateTCPNode(node: ServiceNode) {
    return this.http.post(this.apiUrl+'/tcp', node);
  }

  updateHTTPServerNode(node: ServiceNode) {
    return this.http.post(this.apiUrl+'/http', node);
  }

  updateWebSocketNode(node: ServiceNode) {
    return this.http.post(this.apiUrl+'/websocket', node);
  }

  updateADTNode(node: ServiceNode) {
    return this.http.post(this.apiUrl+'/adt', node);
  }
}
