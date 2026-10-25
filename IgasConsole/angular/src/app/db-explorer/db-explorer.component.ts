import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CoreModule } from '@abp/ng.core';
import { Confirmation, ConfirmationService, ToasterService } from '@abp/ng.theme.shared';
import {
  ColumnInfoDto,
  DbExplorerService,
  TableInfoDto,
  TableRowsDto,
  TableSchemaDto,
} from './db-explorer.service';

@Component({
  selector: 'app-db-explorer',
  standalone: true,
  imports: [FormsModule, CoreModule],
  templateUrl: './db-explorer.component.html',
  styleUrls: ['./db-explorer.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DbExplorerComponent implements OnInit {
  private readonly service = inject(DbExplorerService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly toaster = inject(ToasterService);

  readonly tables = signal<TableInfoDto[]>([]);
  readonly schema = signal<TableSchemaDto | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly skipCount = signal(0);
  readonly pageSize = 25;

  readonly editMode = signal<'insert' | 'update' | null>(null);
  editModel: Record<string, unknown> = {};
  editKey: Record<string, unknown> = {};

  ngOnInit(): void {
    this.service.getTables().subscribe(t => this.tables.set(t));
  }

  onTableChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.cancelEdit();
    this.schema.set(null);
    this.rows.set([]);
    this.skipCount.set(0);
    if (!value) {
      return;
    }
    const table = this.tables().find(t => `${t.schema}.${t.name}` === value);
    if (!table) {
      return;
    }
    this.service.getSchema(table.schema, table.name).subscribe(s => {
      this.schema.set(s);
      this.loadRows();
    });
  }

  loadRows(): void {
    const s = this.schema();
    if (!s) {
      return;
    }
    this.loading.set(true);
    this.service
      .getRows({ schema: s.schema, table: s.name, skipCount: this.skipCount(), maxResultCount: this.pageSize })
      .subscribe({
        next: (r: TableRowsDto) => {
          this.rows.set(r.rows);
          this.totalCount.set(r.totalCount);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }

  nextPage(): void {
    if (this.skipCount() + this.pageSize < this.totalCount()) {
      this.skipCount.update(v => v + this.pageSize);
      this.loadRows();
    }
  }

  prevPage(): void {
    if (this.skipCount() > 0) {
      this.skipCount.update(v => Math.max(0, v - this.pageSize));
      this.loadRows();
    }
  }

  display(value: unknown): string {
    if (value === null || value === undefined) {
      return '';
    }
    if (typeof value === 'object') {
      return JSON.stringify(value);
    }
    return String(value);
  }

  isDisabled(column: ColumnInfoDto): boolean {
    return column.isReadOnly || (this.editMode() === 'update' && column.isPrimaryKey);
  }

  startInsert(): void {
    const s = this.schema();
    if (!s) {
      return;
    }
    this.editModel = {};
    s.columns.forEach(c => (this.editModel[c.name] = ''));
    this.editKey = {};
    this.editMode.set('insert');
  }

  startEdit(row: Record<string, unknown>): void {
    const s = this.schema();
    if (!s) {
      return;
    }
    this.editModel = { ...row };
    this.editKey = {};
    s.columns.filter(c => c.isPrimaryKey).forEach(c => (this.editKey[c.name] = row[c.name]));
    this.editMode.set('update');
  }

  cancelEdit(): void {
    this.editMode.set(null);
    this.editModel = {};
    this.editKey = {};
  }

  save(): void {
    const s = this.schema();
    if (!s) {
      return;
    }
    if (this.editMode() === 'insert') {
      this.service.insert({ schema: s.schema, table: s.name, values: this.editModel }).subscribe({
        next: () => this.afterWrite('Fila insertada'),
        error: e => this.toaster.error(this.errorMessage(e)),
      });
    } else if (this.editMode() === 'update') {
      const values: Record<string, unknown> = {};
      s.columns
        .filter(c => !c.isPrimaryKey && !c.isReadOnly)
        .forEach(c => (values[c.name] = this.editModel[c.name]));
      this.service.update({ schema: s.schema, table: s.name, key: this.editKey, values }).subscribe({
        next: () => this.afterWrite('Fila actualizada'),
        error: e => this.toaster.error(this.errorMessage(e)),
      });
    }
  }

  deleteRow(row: Record<string, unknown>): void {
    const s = this.schema();
    if (!s) {
      return;
    }
    const key: Record<string, unknown> = {};
    s.columns.filter(c => c.isPrimaryKey).forEach(c => (key[c.name] = row[c.name]));

    this.confirmation.warn('¿Borrar esta fila? Esta acción no se puede deshacer.', 'Confirmar borrado').subscribe(status => {
      if (status === Confirmation.Status.confirm) {
        this.service.delete({ schema: s.schema, table: s.name, key }).subscribe({
          next: () => {
            this.toaster.success('Fila borrada');
            this.loadRows();
          },
          error: e => this.toaster.error(this.errorMessage(e)),
        });
      }
    });
  }

  private afterWrite(message: string): void {
    this.toaster.success(message);
    this.cancelEdit();
    this.loadRows();
  }

  private errorMessage(e: unknown): string {
    const err = e as { error?: { error?: { message?: string } } };
    return err?.error?.error?.message ?? 'Ocurrió un error.';
  }
}
