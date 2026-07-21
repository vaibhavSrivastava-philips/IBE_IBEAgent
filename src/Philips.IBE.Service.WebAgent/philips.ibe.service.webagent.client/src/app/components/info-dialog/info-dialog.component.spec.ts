import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { InfoDialogComponent } from './info-dialog.component';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core' ;

describe('InfoDialogComponent', () => {
  let component: InfoDialogComponent;
  let fixture: ComponentFixture<InfoDialogComponent>;

 beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [InfoDialogComponent],
      schemas: [CUSTOM_ELEMENTS_SCHEMA],
    }).compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(InfoDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should emit true when onSelect is called', () => {
    spyOn(component.close, 'emit');
    component.onSelect();
    expect(component.close.emit).toHaveBeenCalledWith(true);
  });

  it('should emit false when dismissForAlert is called', () => {
    spyOn(component.close, 'emit');
    component.dismissForAlert();
    expect(component.close.emit).toHaveBeenCalledWith(false);
  });

  it('should display the title, message, and buttonName', () => {
    component.title = 'Test Title';
    component.message = 'Test Message';
    component.buttonName = 'Test Button';
    fixture.detectChanges();

    const headerElement = fixture.debugElement.query(By.css('dls-dialog-header')).nativeElement;
    const pElement = fixture.debugElement.query(By.css('p')).nativeElement;
    const footer = fixture.debugElement.query(By.css('dls-dialog-footer'));
    const buttons = footer.queryAll(By.css('button'));

    expect(headerElement.textContent).toContain('Test Title');
    expect(pElement.textContent).toContain('Test Message');
    expect(buttons[1].nativeElement.textContent).toContain('Test Button');
  });

  it('should call onSelect when the main button is clicked', () => {
    spyOn(component, 'onSelect');
    const footer = fixture.debugElement.query(By.css('dls-dialog-footer'));
    const mainButton = footer.queryAll(By.css('button'))[1].nativeElement;
    mainButton.click();
    expect(component.onSelect).toHaveBeenCalled();
  });

  it('should call dismissForAlert when the Close button is clicked', () => {
    spyOn(component, 'dismissForAlert');
    const footer = fixture.debugElement.query(By.css('dls-dialog-footer'));
    const closeButton = footer.queryAll(By.css('button'))[0].nativeElement;
    closeButton.click();
    expect(component.dismissForAlert).toHaveBeenCalled();
  });
});
