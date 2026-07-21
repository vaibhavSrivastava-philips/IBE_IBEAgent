import { TestBed } from '@angular/core/testing';
import { LoginService } from './login.service';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { JwtHelperService } from '@auth0/angular-jwt';
import { ResponseModel } from '../models/response-model';
import { JWT_OPTIONS } from '@auth0/angular-jwt';

describe('LoginService', () => {
  let service: LoginService;
  let httpMock: HttpTestingController;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(() => {
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [
        LoginService,
        { provide: Router, useValue: routerSpy },
        JwtHelperService,
        { provide: JWT_OPTIONS, useValue: {} } 
      ]
    });
    service = TestBed.inject(LoginService);
    httpMock = TestBed.inject(HttpTestingController);
    localStorage.clear();
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should encode credentials in base64', () => {
    const encoded = (service as any).utf8ToBase64('user:pass');
    expect(encoded).toBe(btoa(unescape(encodeURIComponent('user:pass'))));
  });

  it('should login and store token and user info', () => {
    const mockResponse: ResponseModel = {
      status: 0,
      value: 'mock.jwt.token',
      displayMessage: 'Success'
    };
    spyOn(service, 'getDecodedAccessToken').and.returnValue('admin');
    service.login('user', 'pass').subscribe(response => {
      expect(response).toEqual(mockResponse);
      expect(localStorage.getItem('token')).toBe('mock.jwt.token');
      expect(localStorage.getItem('isUserLoggedIn')).toBe('true');
      expect(localStorage.getItem('username')).toBe('user');
      expect(localStorage.getItem('role')).toBe('admin');
    });
    const req = httpMock.expectOne('/api/Login');
    expect(req.request.method).toBe('POST');
    req.flush(mockResponse);
  });

  it('should logout and clear data', () => {
    localStorage.setItem('token', 'abc');
    localStorage.setItem('username', 'user');
    localStorage.setItem('isUserLoggedIn', 'true');
    localStorage.setItem('role', 'admin');
    service.logout().subscribe();
    const req = httpMock.expectOne('/api/Login/logout');
    expect(req.request.method).toBe('POST');
    service.clearData();
    expect(localStorage.getItem('token')).toBeNull();
    expect(localStorage.getItem('username')).toBeNull();
    expect(localStorage.getItem('isUserLoggedIn')).toBeNull();
    expect(localStorage.getItem('role')).toBeNull();
  });

  it('should get access token from localStorage', () => {
    localStorage.setItem('token', 'abc');
    expect(service.getAccessToken()).toBe('abc');
  });

  it('should get role from localStorage', () => {
    localStorage.setItem('role', 'admin');
    expect(service.getRole()).toBe('admin');
  });

  it('should update login status', () => {
    service.updateLoginStatus(true);
    expect(localStorage.getItem('isUserLoggedIn')).toBe('true');
    service.updateLoginStatus(false);
    expect(localStorage.getItem('isUserLoggedIn')).toBe('false');
  });

  it('should check if user is logged in', () => {
    localStorage.setItem('isUserLoggedIn', 'true');
    expect(service.isUserLoggedIn()).toBeTrue();
    localStorage.setItem('isUserLoggedIn', 'false');
    expect(service.isUserLoggedIn()).toBeFalse();
  });

  it('should clear session and navigate to root', (done) => {
    spyOn(service, 'logout').and.returnValue({
      subscribe: (handlers: any) => {
        handlers.next({ status: 0 });
      }
    } as any);
    spyOn(service, 'clearData');
    service.clearSession().then(() => {
      expect(service.clearData).toHaveBeenCalled();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/']);
      done();
    });
  });
});
