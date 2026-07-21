import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Contract } from '../models/Contract';

@Injectable({
  providedIn: 'root',
})
export class ContractsService {
  private apiUrl = '/api/Contract';

  constructor(private http: HttpClient) {}

  //getContractById(id: number): Observable<Contract> {
  //  return this.http.get<Contract>(`${this.apiUrl}/${id}`);
  //}

  getAllContract(): Observable<Contract[]> {
    return this.http.get<Contract[]>(`${this.apiUrl}`);
  }

  addContract(data: Contract): Observable<string> {
    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
    });
    return this.http.post(this.apiUrl, data, { headers: headers, responseType: "text" });
  }

  updateContract(oldName: string, data: Contract): Observable<any> {
    const headers = new HttpHeaders({
      'Content-Type': 'application/json',
    });
    return this.http.put(`${this.apiUrl}/${oldName}`, data, { headers: headers, responseType: "text" });
  }

  deleteContract(name:string): Observable<any> {
    return this.http.delete(`${this.apiUrl}/${name}`, {responseType: "text"});
  }
}
