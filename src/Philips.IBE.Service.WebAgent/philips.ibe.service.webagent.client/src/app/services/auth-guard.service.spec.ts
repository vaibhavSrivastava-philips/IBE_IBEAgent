import { TestBed } from '@angular/core/testing';
import { AuthGuardService } from './auth-guard.service';
import { LoginService } from './login.service';
import { Router, ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { of } from 'rxjs';

describe('AuthGuardService', () => {
  let guard: AuthGuardService;
  let loginServiceSpy: jasmine.SpyObj<LoginService>;
  let routerSpy: jasmine.SpyObj<Router>;
  let route: ActivatedRouteSnapshot;
  let state: RouterStateSnapshot;

  beforeEach(() => {
    loginServiceSpy = jasmine.createSpyObj('LoginService', ['isUserLoggedIn', 'getRole']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      providers: [
        AuthGuardService,
        { provide: LoginService, useValue: loginServiceSpy },
        { provide: Router, useValue: routerSpy }
      ]
    });

    guard = TestBed.inject(AuthGuardService);
    route = new ActivatedRouteSnapshot();
    state = { url: '/test' } as RouterStateSnapshot;
  });

  it('should be created', () => {
    expect(guard).toBeTruthy();
  });

  describe('canActivate', () => {
    it('should return true if user is logged in and role matches', () => {
      loginServiceSpy.isUserLoggedIn.and.returnValue(true);
      loginServiceSpy.getRole.and.returnValue('admin');
      route.data = { role: ['admin', 'user'] };
      expect(guard.canActivate(route, state)).toBeTrue();
    });

    it('should navigate to /home and return false if user is logged in but role does not match', () => {
      loginServiceSpy.isUserLoggedIn.and.returnValue(true);
      loginServiceSpy.getRole.and.returnValue('guest');
      route.data = { role: ['admin', 'user'] };
      expect(guard.canActivate(route, state)).toBeFalse();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/home']);
    });

    it('should navigate to /home and return false if user is not logged in', () => {
      loginServiceSpy.isUserLoggedIn.and.returnValue(false);
      route.data = {};
      expect(guard.canActivate(route, state)).toBeFalse();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/home']);
    });
  });

  describe('canActivateChild', () => {
    it('should delegate to canActivate', () => {
      spyOn(guard, 'canActivate').and.returnValue(true);
      expect(guard.canActivateChild(route, state)).toBeTrue();
      expect(guard.canActivate).toHaveBeenCalledWith(route, state);
    });
  });

  describe('canDeactivate', () => {
    it('should always return true', () => {
      expect(guard.canDeactivate({}, route, state)).toBeTrue();
    });
  });

  describe('canLoad', () => {
    it('should always return true', () => {
      expect(guard.canLoad({} as any, [])).toBeTrue();
    });
  });

  describe('checkUserLogin', () => {
    it('should return true if user is logged in and role matches', () => {
      loginServiceSpy.isUserLoggedIn.and.returnValue(true);
      loginServiceSpy.getRole.and.returnValue('admin');
      route.data = { role: ['admin'] };
      expect(guard.checkUserLogin(route, '/test')).toBeTrue();
    });

    it('should navigate to /home and return false if user is logged in but role does not match', () => {
      loginServiceSpy.isUserLoggedIn.and.returnValue(true);
      loginServiceSpy.getRole.and.returnValue('guest');
      route.data = { role: ['admin'] };
      expect(guard.checkUserLogin(route, '/test')).toBeFalse();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/home']);
    });

    it('should navigate to /home and return false if user is not logged in', () => {
      loginServiceSpy.isUserLoggedIn.and.returnValue(false);
      route.data = {};
      expect(guard.checkUserLogin(route, '/test')).toBeFalse();
      expect(routerSpy.navigate).toHaveBeenCalledWith(['/home']);
    });
  });
});
