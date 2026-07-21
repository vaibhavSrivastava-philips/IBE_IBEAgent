import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { HeartBeat } from '../models/HeartBeat';

@Injectable({
  providedIn: 'root',
})

export class HeartBeatService {
  private apiUrlServer = '/api/HeartBeat/server';
  private apiUrlClient = '/api/HeartBeat/client';

  constructor(private http: HttpClient) { }

  checkServerPortOpen(host: string, port: number): Observable<HeartBeat> {

    if (host.includes('http:') || host.includes('https:')) {
      var url = new URL(host);
      let params = new HttpParams()
        .set('host', url.hostname)
        .set('port', port.toString());
      return this.http.get<HeartBeat>(`${this.apiUrlServer}`, { params });
    } else {
      let params = new HttpParams()
        .set('host', host)
        .set('port', port.toString());
      return this.http.get<HeartBeat>(`${this.apiUrlServer}`, { params });
    }




  }

  getTCPPortList(): Observable<Array<string>> {
    return this.http.get<Array<string>>(`${this.apiUrlClient}`);
  }
}
