import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface BackupStatusDto {
  /** Ok | Fallando | Caducada | SoloLocal | SinInformacion */
  state: string;
  lastRunUtc?: string | null;
  ageHours?: number | null;
  dumpBytes: number;
  tables: number;
  remoteSnapshots: number;
  summary: string;
}

export interface RestoreCheckDto {
  /** Ok | Fallando | Caducada | SinInformacion */
  state: string;
  lastRunUtc?: string | null;
  ageDays?: number | null;
  tablesRestored: number;
  summary: string;
}

export interface SystemStatusDto {
  health: string;
  backup: BackupStatusDto;
  restoreCheck: RestoreCheckDto;
  abandonedJobs: number;
  pendingJobs: number;
  activeBlockedIps: number;
  unreadNotifications: number;
  uptimeSeconds: number;
}

@Injectable({ providedIn: 'root' })
export class SystemStatusService {
  private readonly rest = inject(RestService);

  get(): Observable<SystemStatusDto> {
    return this.rest.request<void, SystemStatusDto>(
      { method: 'GET', url: '/api/app/system-status' },
      { apiName: 'Default', skipHandleError: true },
    );
  }
}
