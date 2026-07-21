import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class CertificateService {
  private apiUrl = 'api/certificate'; // Adjust this to match your API URL

  constructor(private http: HttpClient) { }

  upload2Files(file1: File, file2: File, folderName: string): Observable<any> {
    const formData = new FormData();
    formData.append('file1', file1);
    formData.append('file2', file2);

    const params = new HttpParams().set('folderName', folderName);

    return this.http.post(`${this.apiUrl}/multiple`, formData, { params });
  }

  uploadFile(file: File, folderName: string): Observable<any> {
    const formData = new FormData();
    formData.append('file1', file);

    const params = new HttpParams().set('folderName', folderName);

    return this.http.post(`${this.apiUrl}/single`, formData, { params });
  }

  deleteFolder(folderName: string): Observable<any> {
    const params = new HttpParams().set('folderName', folderName);

    return this.http.delete(`${this.apiUrl}/folder`, { params });
  }

  deleteFile(folderName: string, fileName: string): Observable<any> {
    const params = new HttpParams().set('folderName', folderName).set('fileName', fileName);

    return this.http.delete(`${this.apiUrl}/file`, { params });
  }
}