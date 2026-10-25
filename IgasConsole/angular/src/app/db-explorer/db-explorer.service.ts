import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { Observable } from 'rxjs';

export interface TableInfoDto {
  schema: string;
  name: string;
  isEditable: boolean;
}

export interface ColumnInfoDto {
  name: string;
  dataType: string;
  isNullable: boolean;
  isPrimaryKey: boolean;
  isReadOnly: boolean;
  maxLength?: number;
}

export interface TableSchemaDto {
  schema: string;
  name: string;
  isEditable: boolean;
  columns: ColumnInfoDto[];
}

export interface TableRowsDto {
  columns: ColumnInfoDto[];
  rows: Record<string, unknown>[];
  totalCount: number;
}

export interface GetRowsInput {
  schema: string;
  table: string;
  sortColumn?: string;
  sortDescending?: boolean;
  skipCount?: number;
  maxResultCount?: number;
}

/** Cliente del explorador de BD. Solo funciona en Development (el backend lo gatea). */
@Injectable({ providedIn: 'root' })
export class DbExplorerService {
  private readonly rest = inject(RestService);
  private readonly apiName = 'Default';
  private readonly baseUrl = '/api/app/db-explorer';

  getTables(): Observable<TableInfoDto[]> {
    return this.rest.request<void, TableInfoDto[]>(
      { method: 'GET', url: `${this.baseUrl}/tables` },
      { apiName: this.apiName },
    );
  }

  getSchema(schema: string, table: string): Observable<TableSchemaDto> {
    return this.rest.request<void, TableSchemaDto>(
      { method: 'GET', url: `${this.baseUrl}/schema`, params: { schema, table } },
      { apiName: this.apiName },
    );
  }

  getRows(input: GetRowsInput): Observable<TableRowsDto> {
    return this.rest.request<void, TableRowsDto>(
      { method: 'GET', url: `${this.baseUrl}/rows`, params: { ...input } },
      { apiName: this.apiName },
    );
  }

  insert(body: { schema: string; table: string; values: Record<string, unknown> }): Observable<void> {
    return this.rest.request<typeof body, void>(
      { method: 'POST', url: this.baseUrl, body },
      { apiName: this.apiName },
    );
  }

  update(body: {
    schema: string;
    table: string;
    key: Record<string, unknown>;
    values: Record<string, unknown>;
  }): Observable<void> {
    return this.rest.request<typeof body, void>(
      { method: 'PUT', url: this.baseUrl, body },
      { apiName: this.apiName },
    );
  }

  delete(body: { schema: string; table: string; key: Record<string, unknown> }): Observable<void> {
    return this.rest.request<typeof body, void>(
      { method: 'DELETE', url: this.baseUrl, body },
      { apiName: this.apiName },
    );
  }
}
