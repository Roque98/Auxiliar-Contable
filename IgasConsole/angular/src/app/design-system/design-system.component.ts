import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NgClass } from '@angular/common';
import { Confirmation, ConfirmationService, ToasterService } from '@abp/ng.theme.shared';

interface Muestra {
  token: string;
  valor: string;
  uso: string;
  oscuro?: boolean;
}

/**
 * El catálogo vivo del design system de ZIA (solo en desarrollo). Cada pieza se pinta
 * con sus clases reales, así que si algo se ve mal aquí, se ve mal en la aplicación.
 * Las reglas y los tokens están en `src/styles/igas/`.
 */
@Component({
  selector: 'app-design-system',
  standalone: true,
  imports: [NgClass],
  templateUrl: './design-system.component.html',
  styleUrls: ['./design-system.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DesignSystemComponent {
  private readonly avisos = inject(ToasterService);
  private readonly confirmacion = inject(ConfirmationService);

  readonly pestana = signal<'uno' | 'dos'>('uno');

  readonly colores: Muestra[] = [
    { token: '--ig-acento', valor: '#1A7F7F', uso: 'Marca. Botón principal, activo, rayas. Nunca como texto.' },
    { token: '--ig-acento-suave', valor: 'acento 20%', uso: 'Fondo del elemento activo, halo de foco.' },
    { token: '--ig-tinta', valor: '#23262B', uso: 'Texto principal y superficies oscuras.', oscuro: true },
    { token: '--ig-tinta-suave', valor: '#4A4F57', uso: 'Menú y botones de texto.', oscuro: true },
    { token: '--ig-texto-secundario', valor: '#666D75', uso: 'Texto de apoyo (AA 5,2:1).', oscuro: true },
    { token: '--ig-gris', valor: '#8A8F98', uso: 'Decorativo: iconos, rayas. No para texto.', oscuro: true },
    { token: '--ig-gris-medio', valor: '#C9CCD1', uso: 'Bordes discontinuos, iconos vacíos.' },
    { token: '--ig-gris-claro', valor: '#E3E5E8', uso: 'Bordes y separadores.' },
    { token: '--ig-gris-fondo', valor: '#F2F3F5', uso: 'Rellenos: pestañas, hover.' },
    { token: '--ig-fondo', valor: '#FAFAFB', uso: 'Superficie tenue: cabeceras de tabla.' },
    { token: '--ig-exito', valor: '#1E8E5A', uso: 'Éxito (con -suave y -texto).', oscuro: true },
    { token: '--ig-error', valor: '#C0392B', uso: 'Error (con -suave y -texto).', oscuro: true },
  ];

  readonly tipos = [
    { clase: 'ds-t1', nombre: 'Título 1 · 1.6rem · 700', texto: 'Análisis de archivos' },
    { clase: 'ds-t2', nombre: 'Título 2 · 1.05rem · 700', texto: 'Reporte del análisis' },
    { clase: 'ds-t3', nombre: 'Título 3 · 0.95rem · 700', texto: 'Subir archivo' },
    { clase: 'ds-cuerpo', nombre: 'Cuerpo · 15px · 400', texto: 'Sube un archivo y sigue cada paso del análisis.' },
    { clase: 'ds-apoyo', nombre: 'Apoyo · 0.85rem · secundario', texto: 'CSV, TXT, JSON o Excel · Máximo 20 MB' },
  ];

  readonly espacios = [4, 8, 12, 16, 20, 24, 28, 36, 48];
  readonly radios = [
    { token: '--ig-radio-xs', px: 7 },
    { token: '--ig-radio-sm', px: 9 },
    { token: '--ig-radio', px: 10 },
    { token: '--ig-radio-md', px: 12 },
    { token: '--ig-radio-lg', px: 14 },
    { token: '--ig-radio-xl', px: 16 },
  ];

  mostrarAviso(tipo: 'success' | 'error' | 'info'): void {
    const textos = { success: 'Análisis guardado.', error: 'No se pudo guardar.', info: 'Tienes 3 avisos nuevos.' };
    this.avisos[tipo](textos[tipo]);
  }

  mostrarConfirmacion(): void {
    this.confirmacion
      .warn('Se eliminará «ventas julio». Esta acción no se puede deshacer.', '¿Eliminar el análisis?')
      .subscribe((estado: Confirmation.Status) => estado === Confirmation.Status.confirm && this.avisos.success('Eliminado.'));
  }
}
