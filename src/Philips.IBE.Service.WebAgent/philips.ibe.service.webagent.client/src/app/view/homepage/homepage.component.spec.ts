import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HomepageComponent } from './homepage.component';
import { LoginService } from '../../services/login.service';
import { Router } from '@angular/router';

describe('HomepageComponent', () => {
  let component: HomepageComponent;
  let fixture: ComponentFixture<HomepageComponent>;
  let loginServiceSpy: jasmine.SpyObj<LoginService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    loginServiceSpy = jasmine.createSpyObj('LoginService', ['getRole']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    await TestBed.configureTestingModule({
      declarations: [HomepageComponent],
      providers: [
        { provide: LoginService, useValue: loginServiceSpy },
        { provide: Router, useValue: routerSpy }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(HomepageComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should navigate to /home/service if role is admin', () => {
    loginServiceSpy.getRole.and.returnValue('admin');
    component.ngOnInit();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/home/service']);
  });

  it('should navigate to /home/transactions if role is not admin', () => {
    loginServiceSpy.getRole.and.returnValue('user');
    component.ngOnInit();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/home/transactions']);
  });
});
