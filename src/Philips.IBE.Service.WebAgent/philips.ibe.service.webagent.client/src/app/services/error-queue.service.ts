import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ErrorQueue } from '../models/error-queue';

@Injectable({
  providedIn: 'root'
})
export class ErrorQueueService {

  constructor(private http: HttpClient) { }

  public getErrorQueue() :Observable<ErrorQueue[]>{
    return this.http.get<ErrorQueue[]>('api/errorQueue');
  }

  public UpdateErrorQueue(id: number) {
    return this.http.put(`api/errorQueue/${id}`, null);
  }
}
