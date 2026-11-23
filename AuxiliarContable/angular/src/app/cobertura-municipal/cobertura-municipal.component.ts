import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { CoberturaMunicipalDto, CoberturaMunicipalService, csvDeCobertura } from './cobertura-municipal.service';

/**
 * Reporte para el despacho: qué municipios cubre la biblioteca de reglamentos municipales (en todos los estados, los más
 * poblados y formales), cuáles están en proceso y cuáles no; y qué clientes están en un municipio que no cubrimos (de ellos
 * solo sabemos lo federal y lo estatal). Mobile first: clientes arriba, estados plegables abajo.
 */
@Component({
  selector: 'app-cobertura-municipal',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './cobertura-municipal.component.html',
  styleUrls: ['./cobertura-municipal.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoberturaMunicipalComponent implements OnInit {
  private readonly api = inject(CoberturaMunicipalService);

  readonly datos = signal<CoberturaMunicipalDto | null>(null);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.api.reporte().subscribe({
      next: d => this.datos.set(d),
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar el reporte de cobertura.')),
    });
  }

  textoDeCobertura(c: string): string {
    switch (c) {
      case 'no_cubierto':
        return 'Municipio no cubierto';
      case 'parcial':
        return 'Solo algunos reglamentos';
      case 'sin_fuente':
        return 'Su municipio no publica en línea';
      case 'seleccionado':
        return 'En proceso';
      default:
        return 'No se sabe su municipio';
    }
  }

  descargar(): void {
    const d = this.datos();
    if (!d) {
      return;
    }
    const url = URL.createObjectURL(new Blob([csvDeCobertura(d)], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = 'cobertura-municipal.csv';
    a.click();
    URL.revokeObjectURL(url);
  }
}
