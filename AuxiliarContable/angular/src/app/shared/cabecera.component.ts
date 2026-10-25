import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

/**
 * La cabecera de una pantalla: título, una línea de contexto y sitio para acciones.
 *
 * <p>Existe porque las once pantallas del proyecto la escribían **cada una por su cuenta** —el
 * mismo `<h2 class="titulo">` más el mismo `<p class="explicacion">`, con su `.explicacion`
 * repetida en once hojas de estilo casi idénticas—. Eso no es solo duplicación: es la razón
 * práctica de que la aplicación se viera como once pantallas sueltas en vez de como un
 * producto. Cambiar el espaciado o el peso del título obligaba a tocar once archivos, así que
 * en la práctica no se cambiaba nunca.</p>
 *
 * <p>Las acciones van por proyección de contenido en vez de por `@Input`: son botones con su
 * propia lógica, sus permisos y sus estados de carga, y pasarlos como datos habría convertido
 * esto en un mini-framework de botones. Con `<ng-content>` cada pantalla sigue poniendo lo
 * suyo y lo único compartido es dónde se colocan.</p>
 */
@Component({
  selector: 'app-cabecera',
  standalone: true,
  template: `
    <header class="cabecera">
      <div class="cabecera-texto">
        <h1 class="cabecera-titulo">{{ titulo }}</h1>
        @if (descripcion) {
          <p class="cabecera-descripcion">{{ descripcion }}</p>
        }
        <!-- Para las descripciones con formato: dos de las once llevan <b> y varias claves
             de traducción dentro, y eso no cabe en un @Input de texto. Se proyectan tal cual
             y heredan el estilo por el selector de abajo. -->
        <div class="cabecera-descripcion"><ng-content select="[descripcion]" /></div>
      </div>
      <div class="cabecera-acciones">
        <ng-content />
      </div>
    </header>
  `,
  styles: [
    `
      /* El encabezado de página del design system (\`.ig-encabezado\`). */
      .cabecera {
        display: flex;
        flex-wrap: wrap;
        align-items: flex-end;
        justify-content: space-between;
        gap: var(--ig-esp-4);
        margin-bottom: var(--ig-esp-7);
      }

      .cabecera-texto {
        /* min-width:0 para que un título largo no ensanche la fila y empuje las acciones
           fuera de la pantalla en móvil. */
        min-width: 0;
        flex: 1 1 16rem;
      }

      .cabecera-titulo {
        font-size: var(--ig-titulo-1);
        font-weight: var(--ig-peso-fuerte);
        letter-spacing: var(--ig-tracking-titulo);
        margin: 0 0 var(--ig-esp-1);
      }

      .cabecera-descripcion {
        margin: 0;
        color: var(--ig-texto-secundario);
        max-width: 60ch;
      }

      /* El contenedor del hueco proyectado no debe ocupar sitio cuando nadie proyecta nada:
         si no, todas las cabeceras sin descripción rica cargarían con un margen fantasma. */
      .cabecera-descripcion:has(> :not(:empty)) {
        display: block;
      }

      .cabecera-descripcion:not(:has(*)) {
        display: none;
      }

      .cabecera-acciones {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 10px;
        margin-left: auto;
      }

      @media (max-width: 760px) {
        .cabecera-titulo {
          font-size: 1.3rem;
        }

        .cabecera-acciones {
          width: 100%;
        }
      }

      .cabecera-acciones:empty {
        display: none;
      }
    `,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CabeceraComponent {
  @Input({ required: true }) titulo = '';

  /** Opcional: hay pantallas que se explican solas con el título. */
  @Input() descripcion?: string | null;
}
