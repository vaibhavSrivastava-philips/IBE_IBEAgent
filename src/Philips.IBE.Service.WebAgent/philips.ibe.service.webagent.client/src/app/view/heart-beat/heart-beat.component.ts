import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CommunicationDataService } from '../../services/communication-data.service';
import { HeartBeatService } from '../../services/heartbeat.service';
import { forkJoin, Observable, of, Subscription, timer } from 'rxjs';
import { catchError, map, switchMap, tap } from 'rxjs/operators';

interface CommunicationData {
  id: number;
  name: string;
  mode: string;
  type: string;
  ipAddress: string;
  port: string;
  status: boolean;
}

@Component({
  selector: 'app-heart-beat',
  templateUrl: './heart-beat.component.html',
  styleUrls: ['./heart-beat.component.scss'],
  standalone: true,
  imports: [
    CommonModule
  ]
})
export class HeartBeatComponent implements OnInit, OnDestroy {
  allCommunicationData: CommunicationData[] = [];
  filteredCommunicationData: CommunicationData[] = [];
  errorMessage: string = "";
  private statusCheckSubscription: Subscription | null = null;

  constructor(
    private communicationDataService: CommunicationDataService, 
    private heartBeatService: HeartBeatService,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit() {
    this.getAllCommunicationData();
    this.startPeriodicStatusCheck();
  }

  ngOnDestroy() {
    this.stopPeriodicStatusCheck();
  }

  getAllCommunicationData(): void {
    this.communicationDataService.getAllCommunicationData().subscribe({
      next: (data) => {
       // this.allCommunicationData = data.map(item => ({...item, status: false}));
        this.filteredCommunicationData = [...this.allCommunicationData];
        console.log('All communication data:', this.allCommunicationData);
        this.checkAllStatuses();
      },
      error: (error) => {
        console.error('Error fetching all communication data:', error);
      }
    });
  }

  checkAllStatuses(): void {
    const serverStatusChecks: Observable<boolean>[] = this.allCommunicationData
      .filter(item => item.mode === 'server')
      .map(item => 
        this.heartBeatService.checkServerPortOpen(item.ipAddress, parseInt(item.port))
          .pipe(
            tap(heartbeat => {
              // console.log(`Server ${item.name} (${item.ipAddress}:${item.port}) status:`, heartbeat.isOpen);
              item.status = heartbeat.isOpen;
            }),
            map(heartbeat => heartbeat.isOpen),
            catchError(error => {
              //console.error(`Error checking server ${item.name} (${item.ipAddress}:${item.port}):`, error);
              item.status = false;
              return of(false);
            })
          )
      );

      const clientStatusChecks: Observable<boolean>[] = this.allCommunicationData
      .filter(item => item.mode === 'client')
      .map(item => 
        this.heartBeatService.getTCPPortList()
          .pipe(
            tap((response: any) => {
              // console.log(`Received TCP port list for ${item.name}:`, response);
            }),
            map((response: any) => {
              if (response && Array.isArray(response.tcpPorts)) {
                const portList = response.tcpPorts;
                item.status = portList.includes(item.port);
                // console.log(`Client ${item.name} (port ${item.port}) status:`, item.status);
              } else {
                console.error(`Unexpected port list format for ${item.name} (port ${item.port}):`, response);
                item.status = false;
              }
              return item.status;
            }),
            catchError(error => {
              //console.error(`Error processing TCP port list for ${item.name} (port ${item.port}):`, error);
              item.status = false;
              return of(false);
            })
          )
      );
  
    forkJoin([...serverStatusChecks, ...clientStatusChecks]).subscribe({
      next: (results) => {
        console.log('All status check results:', results);
        console.log('Updated communication data:', this.allCommunicationData);
        this.cdr.detectChanges(); // Force change detection
      },
      error: (error) => {
        this.errorMessage = `Error checking statuses: ${error.message}`;
        console.error('Error in forkJoin:', error);
      },
      complete: () => {
        console.log('Status check completed');
      }
    });
  }
  
  startPeriodicStatusCheck(): void {
    this.statusCheckSubscription = timer(0, 30000).subscribe(() => {
      this.checkAllStatuses();
    });
  }

  stopPeriodicStatusCheck(): void {
    if (this.statusCheckSubscription) {
      this.statusCheckSubscription.unsubscribe();
      this.statusCheckSubscription = null;
    }
  }

  refreshAllStatuses(): void {
    console.log("Refreshing all statuses");
    this.checkAllStatuses();
  }

  filterGlobal(value: string, matchMode: string) {
    this.filteredCommunicationData = this.allCommunicationData.filter(item => 
      Object.values(item).some(val => 
        val && val.toString().toLowerCase().includes(value.toLowerCase())
      )
    );
  }
}