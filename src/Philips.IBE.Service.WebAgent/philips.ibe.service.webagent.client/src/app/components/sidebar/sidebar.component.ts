import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { ListComponent, ListItemComponent} from '@filament/angular';

@Component({
  selector: 'app-sidebar',
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.scss'],
  standalone: true,
  imports: [
    CommonModule,
    ListComponent,
    ListItemComponent
  ]
})
export class SidebarComponent implements OnInit {
  public role: string = '';
  constructor(private router: Router) { 
    this.role = localStorage.getItem('role') || '';
    if(this.role === 'normal'){
      this.handleClick('transactions');
    }
  }

  ngOnInit() {
  }
  
  handleClick(destination: string) {
    let url = '/home/'+destination;
    this.router.navigateByUrl(url);
  }


}
