import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LoginComponent } from './login.component';
import { LoginService } from '../../services/login.service';
import { NotificationService } from '../../services/notification.service';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core'; 

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let loginServiceSpy: jasmine.SpyObj<LoginService>;
  let notificationServiceSpy: jasmine.SpyObj<NotificationService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    loginServiceSpy = jasmine.createSpyObj('LoginService', ['clearData', 'login']);
    notificationServiceSpy = jasmine.createSpyObj('NotificationService', ['showMessage']);
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);

    await TestBed.configureTestingModule({
      declarations: [LoginComponent],
      providers: [
        { provide: LoginService, useValue: loginServiceSpy },
        { provide: NotificationService, useValue: notificationServiceSpy },
        { provide: Router, useValue: routerSpy }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA] 
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should clear data on init', () => {
    component.ngOnInit();
    expect(loginServiceSpy.clearData).toHaveBeenCalled();
  });

  it('should navigate and show message on successful login', () => {
    component.username = 'testuser';
    component.password = 'dummyPassword123!';
    loginServiceSpy.login.and.returnValue(of({
      value: 'user',
      status: 1,
      displayMessage: 'Login successful'
    }));
    component.onSubmit();

    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/home');
    expect(notificationServiceSpy.showMessage).toHaveBeenCalledWith('success', 'Welcome testuser', '');
    expect(component.errorMessage).toBe('');
  });

  it('should set errorMessage if user is not authorized', () => {
    component.username = 'testuser';
    component.password = 'dummyPassword123!';
    loginServiceSpy.login.and.returnValue(of({
      value: [],
      status: 0,
      displayMessage: 'User is not authorized'
    }));
    component.onSubmit();

    expect(component.errorMessage).toBe('User is not authorized');
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
    expect(notificationServiceSpy.showMessage).not.toHaveBeenCalled();
  });

  it('should set errorMessage on login error', () => {
    component.username = 'testuser';
    component.password = 'dummyPassword123!';
    loginServiceSpy.login.and.returnValue(throwError(() => 'error'));

    component.onSubmit();

    expect(component.errorMessage).toBe('Invalid username or password');
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
    expect(notificationServiceSpy.showMessage).not.toHaveBeenCalled();
  });
});
