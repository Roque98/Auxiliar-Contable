import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';

interface Pestana {
  ruta: string;
  nombre: string;
  icono: string;
  /** Las formas de dar esa información (sub-pestañas). */
  formas: { ruta: string; nombre: string }[];
}

/**
 * «Mi información»: todo lo que el cliente le da al despacho, en un solo lugar. Facturas (subir XML o PDF, pedir
 * factura), Dinero (estados de cuenta y comprobantes, efectivo y crédito), Control interno (tickets y notas, el
 * expediente de cada gasto) y Todo (cada operación con sus tres patas). El cliente elegido viaja en `?cliente=`.
 */
@Component({
  selector: 'app-mi-informacion',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './mi-informacion.component.html',
  styleUrls: ['./mi-informacion.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MiInformacionComponent {
  private readonly router = inject(Router);

  readonly pestanas: Pestana[] = [
    {
      ruta: 'facturas', nombre: 'Facturas', icono: 'bi-receipt',
      formas: [
        { ruta: 'facturas', nombre: 'Subir XML o PDF · Mis facturas' },
        { ruta: 'facturas/pedir', nombre: 'Pedir factura' },
      ],
    },
    {
      ruta: 'dinero', nombre: 'Dinero', icono: 'bi-bank',
      formas: [
        { ruta: 'dinero', nombre: 'Estados de cuenta y transferencias' },
        { ruta: 'dinero/efectivo', nombre: 'Efectivo y crédito' },
      ],
    },
    {
      ruta: 'control', nombre: 'Control interno', icono: 'bi-clipboard-check',
      formas: [
        { ruta: 'control', nombre: 'Tickets y notas' },
        { ruta: 'control/expedientes', nombre: 'Expediente de cada gasto' },
      ],
    },
    { ruta: 'todo', nombre: 'Todo', icono: 'bi-diagram-3', formas: [] },
  ];

  /** La pestaña en la que se está (primer tramo después de /mi-informacion). */
  private readonly seccion = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      startWith(null),
      map(() => this.router.url.split('?')[0].split('/')[2] ?? 'facturas'),
    ),
    { initialValue: 'facturas' },
  );

  formas(): Pestana['formas'] {
    return this.pestanas.find(p => p.ruta === this.seccion())?.formas ?? [];
  }
}
