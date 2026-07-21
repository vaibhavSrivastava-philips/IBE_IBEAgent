import { TestBed } from '@angular/core/testing';
import { AuthInterceptorService } from './auth-interceptor.service';
import { LoginService } from './login.service';
import { Router } from '@angular/router';
import { HttpRequest, HttpHandler, HttpEvent, HttpResponse, HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

describe('AuthInterceptorService', () => {
  let service: AuthInterceptorService;
  let loginServiceSpy: jasmine.SpyObj<LoginService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let httpHandlerSpy: jasmine.SpyObj<HttpHandler>;

  beforeEach(() => {
    loginServiceSpy = jasmine.createSpyObj('LoginService', ['getAccessToken', 'clearSession', 'clearData']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);
    httpHandlerSpy = jasmine.createSpyObj('HttpHandler', ['handle']);

    TestBed.configureTestingModule({
      providers: [
        AuthInterceptorService,
        { provide: LoginService, useValue: loginServiceSpy },
        { provide: Router, useValue: routerSpy }
      ]
    });

    service = TestBed.inject(AuthInterceptorService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should add Authorization header if access token exists', (done) => {
    loginServiceSpy.getAccessToken.and.returnValue('test-token');
    const req = new HttpRequest('GET', '/test');
    httpHandlerSpy.handle.and.callFake((request: HttpRequest<any>) => {
      expect(request.headers.get('Authorization')).toBe('Bearer test-token');
      return of(new HttpResponse({ status: 200 }));
    });

    service.intercept(req, httpHandlerSpy).subscribe(() => done());
  });

  it('should not add Authorization header if access token does not exist', (done) => {
    loginServiceSpy.getAccessToken.and.returnValue('');
    const req = new HttpRequest('GET', '/test');
    httpHandlerSpy.handle.and.callFake((request: HttpRequest<any>) => {
      expect(request.headers.has('Authorization')).toBeFalse();
      return of(new HttpResponse({ status: 200 }));
    });

    service.intercept(req, httpHandlerSpy).subscribe(() => done());
  });

  it('should call clearSession on 401 error', (done) => {
    loginServiceSpy.getAccessToken.and.returnValue('test-token');
    spyOn(service as any, 'addAuthorizationHeader').and.callThrough();
    const req = new HttpRequest('GET', '/test');
    const errorResponse = new HttpErrorResponse({ status: 401, statusText: 'Unauthorized' });

    httpHandlerSpy.handle.and.returnValue(throwError(() => errorResponse));

    service.intercept(req, httpHandlerSpy).subscribe({
      error: (err) => {
        expect(loginServiceSpy.clearSession).toHaveBeenCalled();
        expect(err.status).toBe(401);
        done();
      }
    });
  });

  it('should not call clearSession on non-401 error', (done) => {
    loginServiceSpy.getAccessToken.and.returnValue('test-token');
    const req = new HttpRequest('GET', '/test');
    const errorResponse = new HttpErrorResponse({ status: 500, statusText: 'Server Error' });

    httpHandlerSpy.handle.and.returnValue(throwError(() => errorResponse));

    service.intercept(req, httpHandlerSpy).subscribe({
      error: (err) => {
        expect(loginServiceSpy.clearSession).not.toHaveBeenCalled();
        expect(err.status).toBe(500);
        done();
      }
    });
  });


  it('clearSession should clear data, navigate to login, and alert', () => {
    spyOn(window, 'alert');
    service.clearSession();
    expect(loginServiceSpy.clearData).toHaveBeenCalled();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
    expect(window.alert).toHaveBeenCalledWith('Session Expired');
  });
});
