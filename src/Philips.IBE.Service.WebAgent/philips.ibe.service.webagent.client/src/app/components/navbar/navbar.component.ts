import { Component } from '@angular/core';
import { AvatarComponent, TextComponent } from '@filament/angular';
import { PersonPortraitIconComponent, LogOutIconComponent } from '@filament-icons/angular';
import { LoginService } from '../../services/login.service';

interface MenuItem {
  label: string;
}

@Component({
  selector: 'app-navbar',
  templateUrl: './navbar.component.html',
  styleUrl: './navbar.component.scss',
  standalone: true,
  imports: [
    AvatarComponent,
    PersonPortraitIconComponent,
    LogOutIconComponent,
    TextComponent
  ]
})
export class NavbarComponent {
  public singleOn: boolean = true;
  public showSidebar: boolean = false;
  public items: MenuItem[] = this.categoryItems();
  public selected: MenuItem | undefined;
  public username: string = '';
 

  constructor(
    private loginService: LoginService
  ) {
    this.username = localStorage.getItem('username') || '';
    
  }

  toggleSidebar() {
    this.showSidebar = !this.showSidebar;
  }

 
  logout(){
    this.loginService.clearSession();      
  }

  categoryItems(): MenuItem[] {
    return [
      {
        label: "c1"
      },
      {
        label: "c2"
      },
      {
        label: "c3"
      }
    ]
  }
}
