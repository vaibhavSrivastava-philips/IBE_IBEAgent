import { Router } from '@angular/router';
import { Injectable } from '@angular/core';
import { HttpBackend, HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';
import { JwtHelperService } from '@auth0/angular-jwt';
import { ResponseModel } from '../models/response-model';



@Injectable({
  providedIn: 'root',
})
export class LoginService {
  private apiUrl = '/api/Login';
  private accessToken: string = "";
  constructor(
    private http: HttpClient,
    private handler: HttpBackend,
    private router: Router
  ) {
    this.http = new HttpClient(handler);
  }

  private utf8ToBase64(utf8String: string): string {
    // Encode the string as a URI component to handle special UTF-8 characters
    const utf8Bytes = new TextEncoder().encode(utf8String);
  
    // Convert the byte array to a Base64 string
    let base64String = btoa(String.fromCharCode(...utf8Bytes));
  
    return base64String;
  }

  login(username: string, password: string): Observable<ResponseModel> {
    const credentials = this.utf8ToBase64(`${username}:${password}`);
    const headers = new HttpHeaders({
      'Authorization': `Basic ${credentials}`,
      'Content-Type': 'application/json'
    });

    return this.http.post<ResponseModel>(this.apiUrl, null, { headers }).pipe(
      tap((response) => {
        if (response.status === 0 && response.value) {
          localStorage.setItem('token', response.value);
          localStorage.setItem('isUserLoggedIn', 'true');
          localStorage.setItem('username', username);
          localStorage.setItem('role', this.getDecodedAccessToken(response.value));
        }
      })
    );
  }

  logout() {
    const token = this.getAccessToken();
    const headers = new HttpHeaders({
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    });
    return this.http.post(this.apiUrl + "/logout", null, { headers });
  }

  clearData() {
    localStorage.removeItem('token');
    localStorage.removeItem('username');
    localStorage.removeItem('isUserLoggedIn');
    localStorage.removeItem('role');
    this.accessToken ="";
  }


  getAccessToken(): string {
    if (this.accessToken === "") {
      this.accessToken = localStorage.getItem("token") || "";
    }
    return this.accessToken;
  }

  getRole(): string {
    return localStorage.getItem('role') || '';
  }

  updateLoginStatus(status: boolean): void {
    if (!status) {
      localStorage.setItem('isUserLoggedIn', 'false');
    } else {
      localStorage.setItem('isUserLoggedIn', 'true');
    }
  }


  isUserLoggedIn(): boolean {
    return localStorage.getItem('isUserLoggedIn') === 'true';
  }

  getDecodedAccessToken(token: string): any {
    const helper = new JwtHelperService();
    const decodedToken = helper.decodeToken(token);
    let role = getRoleFromToken(decodedToken);
    return role;
  }

  async clearSession(){

    let output = await this.logout().subscribe({
      next: (response) => {
        this.clearData();
        let result: ResponseModel = {
          value: null,
          status: 0,
          displayMessage: 'Logout successful'
        }
        return result;
      },
      error: (error) => {
        console.log('Logout failed:', error);
        let result: ResponseModel = {
          value: null,
          status: 1,
          displayMessage: 'Logout failed'
        }
        return result;
      }
    });
    console.log("Session Expired");
    this.router.navigate(['/']);
    return output;
  }
}

function getRoleFromToken(decodedToken: any) {
  let role = '';
  if (decodedToken['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']) {
    role = decodedToken['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'];
  }
  return role;
}
