import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DeleteAlertComponent } from './delete-alert.component';
import { DialogModule } from 'primeng/dialog';

describe('DeleteAlertComponent', () => {
  let component: DeleteAlertComponent;
  let fixture: ComponentFixture<DeleteAlertComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [DeleteAlertComponent],
      imports: [
        DialogModule
      ]
    }).compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(DeleteAlertComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should emit true when onDelete is called', () => {
    spyOn(component.close, 'emit');
    component.onDelete();
    expect(component.close.emit).toHaveBeenCalledWith(true);
  });

  it('should emit false when dismissForAlert is called', () => {
    spyOn(component.close, 'emit');
    component.dismissForAlert();
    expect(component.close.emit).toHaveBeenCalledWith(false);
  });

  it('should display the commpointName', () => {
    component.commpointName = 'Test Point';
    fixture.detectChanges();
    const pElement = fixture.debugElement.query(By.css('p')).nativeElement;
    expect(pElement.textContent).toContain('Test Point');
  });

  it('should call onDelete when the Delete button is clicked', () => {
    spyOn(component, 'onDelete');
    const deleteButton = fixture.debugElement.queryAll(By.css('button'))[1].nativeElement;
    deleteButton.click();
    expect(component.onDelete).toHaveBeenCalled();
  });

  it('should call dismissForAlert when the Cancel button is clicked', () => {
    spyOn(component, 'dismissForAlert');
    const cancelButton = fixture.debugElement.queryAll(By.css('button'))[0].nativeElement;
    cancelButton.click();
    expect(component.dismissForAlert).toHaveBeenCalled();
  });
});
