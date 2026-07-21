import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  ButtonComponent,
  CardComponent,
  FlexBoxComponent,
  TextFieldComponent,
  PasswordComponent,
  InputDirective
} from '@filament/angular';
import {
  PhilipsLogoIconComponent,
  OperatorsManualISOIconComponent,
  CautionISOIconComponent
} from '@filament-icons/angular';
import { Router } from '@angular/router';
import { LoginService } from '../../services/login.service';
import { NotificationService } from '../../services/notification.service';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  imports: [
    FormsModule,
    ButtonComponent,
    CardComponent,
    FlexBoxComponent,
    TextFieldComponent,
    PasswordComponent,
    InputDirective,
    PhilipsLogoIconComponent,
    OperatorsManualISOIconComponent,
    CautionISOIconComponent
  ]
})
export class LoginComponent implements OnInit{
  hidePasswordIcon:boolean = false;
  message: any;

  username: string = '';
  password: string = '';
  errorMessage: string = '';
  isShow: boolean = false;

  constructor(
    private loginService: LoginService, 
    private router: Router,
    private notificationService: NotificationService
  ) {}

  ngOnInit(): void {
    this.loginService.clearData();
  }

  onSubmit() {
    this.loginService.login(this.username, this.password).subscribe({
      next: (response) => {
        if (response.value.length > 0) {          
          this.router.navigateByUrl('/home');
          this.notificationService.showMessage('success', 'Welcome '+this.username, '');
        }
        else{
          this.errorMessage = 'User is not authorized';
        }
      },
      error: (error) => {
        console.log('Login failed:', error);
        this.errorMessage = 'Invalid username or password';
      },
    });
  }
}
