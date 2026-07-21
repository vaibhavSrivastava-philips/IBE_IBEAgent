import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { LoginComponent } from './view/login/login.component';
import { CommunicationPointComponent } from './view/communication-point/communication-point.component';
import { ContractComponent } from './view/contract/contract.component';
import { HomepageComponent } from './view/homepage/homepage.component';
import { ErrorQueueComponent } from './view/error-queue/error-queue.component';
import { HeartBeatComponent } from './view/heart-beat/heart-beat.component';
import { ServiceConfigurationComponent } from './view/service-configuration/service-configuration.component';
import { AuthGuardService } from './services/auth-guard.service';

const routes: Routes =
  [
    {
      path: '',
      component: LoginComponent
    },
    {
      path: 'home',
      component: HomepageComponent,
      children: [
        {
          path: 'service',
          component: ServiceConfigurationComponent,
          canActivate: [AuthGuardService],
          data: {
            role: 'admin'
          }
        },
        {
          path: 'commpoints',
          component: CommunicationPointComponent,
          canActivate: [AuthGuardService],
          data: {
            role: 'admin'
          }
        },
        {
          path: 'contracts',
          component: ContractComponent,
          canActivate: [AuthGuardService],
          data: {
            role: 'admin'
          }
        },
        {
          path: 'transactions',
          component: ErrorQueueComponent,
          canActivate: [AuthGuardService],
          data: {
            role: 'normal'
          }
        },
        {
          path: 'heartbeat',
          component: HeartBeatComponent,
          canActivate: [AuthGuardService],
          data: {
            role: 'normal'
          }
        }
      ]
    }
  ];


@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
