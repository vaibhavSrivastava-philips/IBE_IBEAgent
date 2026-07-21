import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CommunicationData } from '../models/CommunicationData';

@Injectable({
  providedIn: 'root',
})
export class CommunicationDataService {
  private apiUrl = '/api/CommunicationPoint';

  constructor(private http: HttpClient) {}

  getCommunicationDataById(id: number): Observable<CommunicationData> {
    return this.http.get<CommunicationData>(`${this.apiUrl}/${id}`);
  }

  getAllCommunicationData(): Observable<CommunicationData[]> {
    return this.http.get<CommunicationData[]>(`${this.apiUrl}`);
  }

  addCommunicationData(data: CommunicationData): Observable<string> {
    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
    });
    return this.http.post(this.apiUrl, data, { headers: headers, responseType: "text" });
  }

  updateCommunicationData(id: number, data: CommunicationData): Observable<any> {
    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
    });
    return this.http.put(`${this.apiUrl}/${id}`, data, { headers: headers, responseType: "text" });
  }

  deleteCommunicationData(id: number): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${id}`, {responseType: "text"});
  }
}
