import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NavbarComponent } from './navbar.component';
import { LoginService } from '../../services/login.service';

class MockLoginService {
  clearSession() {

  }
}

describe('NavbarComponent', () => {
  let component: NavbarComponent;
  let fixture: ComponentFixture<NavbarComponent>;
  let loginService: LoginService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [NavbarComponent],
      providers: [
        { provide: LoginService, useClass: MockLoginService }
      ]
    }).compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(NavbarComponent);
    component = fixture.componentInstance;
    loginService = TestBed.inject(LoginService);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize username from localStorage', () => {
    localStorage.setItem('username', 'testuser');
    const newFixture = TestBed.createComponent(NavbarComponent);
    const newComponent = newFixture.componentInstance;
    expect(newComponent.username).toBe('testuser');
    localStorage.removeItem('username'); 
  });

  it('should have an empty username if localStorage is empty', () => {
    expect(component.username).toBe('');
  });

  it('should toggle showSidebar property', () => {
    expect(component.showSidebar).toBe(false);
    component.toggleSidebar();
    expect(component.showSidebar).toBe(true);
    component.toggleSidebar();
    expect(component.showSidebar).toBe(false);
  });

  it('should call loginService.clearSession on logout', () => {
    spyOn(loginService, 'clearSession');
    component.logout();
    expect(loginService.clearSession).toHaveBeenCalled();
  });

  it('should return category items', () => {
    const items: any[] = component.categoryItems();
    expect(items.length).toBe(3);
    expect(items[0].label).toBe('c1');
    expect(items[1].label).toBe('c2');
    expect(items[2].label).toBe('c3');
  });


});
