import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { SidebarComponent } from './sidebar.component';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core'; 

class MockRouter {
  navigateByUrl(url: string) {
    return url;
  }
}

describe('SidebarComponent', () => {
  let component: SidebarComponent;
  let fixture: ComponentFixture<SidebarComponent>;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [SidebarComponent],
      providers: [
        { provide: Router, useClass: MockRouter } 
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
  });

  beforeEach(() => {
    localStorage.clear();
    fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should initialize role from localStorage', () => {
    localStorage.setItem('role', 'admin');
    fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
    expect(component.role).toBe('admin');
  });

  it('should have an empty role if localStorage is empty', () => {
    fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
    expect(component.role).toBe('');
  });

  it('should call handleClick with \'transactions\' if role is \'normal\'', () => {
    localStorage.setItem('role', 'normal');
    spyOn(SidebarComponent.prototype, 'handleClick').and.callThrough();
    fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
    expect(SidebarComponent.prototype.handleClick).toHaveBeenCalledWith('transactions');
  });

  it('should NOT call handleClick if role is not \'normal\'', () => {
    localStorage.setItem('role', 'admin');
    spyOn(SidebarComponent.prototype, 'handleClick');
    fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
    expect(SidebarComponent.prototype.handleClick).not.toHaveBeenCalled();
  });

  it('should navigate to the correct URL when handleClick is called', () => {
    spyOn(router, 'navigateByUrl');
    const destination = 'dashboard';
    component.handleClick(destination);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/home/dashboard');
  });
});
