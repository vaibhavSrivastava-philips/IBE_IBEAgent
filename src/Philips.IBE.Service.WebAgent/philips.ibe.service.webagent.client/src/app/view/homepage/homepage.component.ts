import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, Router } from '@angular/router';
import { LoginService } from '../../services/login.service';
import { NavbarComponent } from '../../components/navbar/navbar.component';
import { SidebarComponent } from '../../components/sidebar/sidebar.component';
import { ShadowDirective } from '@filament/angular';

@Component({
  selector: 'app-homepage',
  templateUrl: './homepage.component.html',
  styleUrls: ['./homepage.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    NavbarComponent,
    SidebarComponent,
    ShadowDirective
  ]
})
export class HomepageComponent implements OnInit {

  constructor(
    public loginService: LoginService,
    public router: Router
  ) { }

  ngOnInit() {
    let role = this.loginService.getRole();
    if (role === 'admin') {
      this.router.navigate(['/home/service']);
    } else {
      this.router.navigate(['/home/transactions']);
    }
  }

}
