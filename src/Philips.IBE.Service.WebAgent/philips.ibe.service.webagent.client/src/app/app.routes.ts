import { Route, Routes } from '@angular/router';
import { LoginComponent } from './view/login/login.component';
import { CommunicationPointComponent } from './view/communication-point/communication-point.component';
import { ContractComponent } from './view/contract/contract.component';
import { HomepageComponent } from './view/homepage/homepage.component';
import { ErrorQueueComponent } from './view/error-queue/error-queue.component';
import { HeartBeatComponent } from './view/heart-beat/heart-beat.component';
import { ServiceConfigurationComponent } from './view/service-configuration/service-configuration.component';
import { AuthGuardService } from './services/auth-guard.service';

function guardedRoute(path: string, component: Route['component'], role: string): Route {
  return { path, component, canActivate: [AuthGuardService], data: { role } };
}

export const routes: Routes = [
  {
    path: '',
    component: LoginComponent
  },
  {
    path: 'home',
    component: HomepageComponent,
    children: [
      guardedRoute('service', ServiceConfigurationComponent, 'admin'),
      guardedRoute('commpoints', CommunicationPointComponent, 'admin'),
      guardedRoute('contracts', ContractComponent, 'admin'),
      guardedRoute('transactions', ErrorQueueComponent, 'normal'),
      guardedRoute('heartbeat', HeartBeatComponent, 'normal'),
    ]
  }
];
