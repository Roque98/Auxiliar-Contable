import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ConciliacionService } from '../conciliacion/conciliacion.service';
import { DocumentosService, MESES, ResumenDelMesDto } from '../documentos/documentos.service';
import { TipoDeDocumento } from '../documentos/tipos';
import { mensajeDeError } from '../invitaciones/invitaciones.service';
import { MesaService, SaldosDeAperturaDto } from '../mesa/mesa.service';
import { ClienteVisibleDto, PerfilFiscalService } from '../perfil-fiscal/perfil-fiscal.service';
import { Apertura, CuentaDelCatalogo, aApertura, catalogoAXml, leerBalanzaTabla, leerBalanzaXml, leerCatalogo, leerCatalogoTabla, mesSiguiente, partirCsv } from './balanza';
import { ES_EXCEL, filasDeExcel } from './excel';

type Zona = 'facturas' | 'estados' | 'control' | 'nomina';

interface ZonaDeCarga {
  clave: Zona;
  titulo: string;
  icono: string;
  ayuda: string;
  acepta: string;
  /** Tipos de la bóveda que cuentan como «ya cargado» en esta zona. */
  tipos: TipoDeDocumento[];
}

interface Avance {
  total: number;
  hechos: number;
  duplicados: number;
  errores: { nombre: string; motivo: string }[];
}

const ES_XML = (a: File) => /\.xml$/i.test(a.name) || a.type.includes('xml');

/**
 * Conciliación mensual: el auxiliar del despacho recibe todo el mes de un cliente y lo carga en su apartado (facturas,
 * estados de cuenta, control interno, nómina) sin que se le pregunte cómo se relaciona cada cosa —eso es del trabajo
 * diario—. Antes de conciliar contesta con qué catálogo de cuentas y con qué saldos se arranca (balanza o ceros), y
 * entonces corre la conciliación de todo (el botón «Conciliar» de Cierre del mes).
 */
@Component({
  selector: 'app-conciliacion-mensual',
  standalone: true,
  imports: [FormsModule, CurrencyPipe, DatePipe],
  templateUrl: './conciliacion-mensual.component.html',
  styleUrls: ['./conciliacion-mensual.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConciliacionMensualComponent implements OnInit {
  private readonly perfiles = inject(PerfilFiscalService);
  private readonly boveda = inject(DocumentosService);
  private readonly mesa = inject(MesaService);
  private readonly conciliacion = inject(ConciliacionService);
  private readonly ruta = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly meses = MESES;
  readonly anios = (() => {
    const y = new Date().getFullYear();
    return [y, y - 1, y - 2, y - 3, y - 4, y - 5];
  })();

  readonly zonas: ZonaDeCarga[] = [
    {
      clave: 'facturas', titulo: 'Facturas', icono: 'bi-receipt',
      ayuda: 'Los XML emitidos y recibidos del mes (y sus PDF, si los tienes). Cada XML se acomoda en el mes de su fecha.',
      acepta: '.xml,.pdf,application/xml,text/xml,application/pdf', tipos: [TipoDeDocumento.Cfdi],
    },
    {
      clave: 'estados', titulo: 'Estados de cuenta', icono: 'bi-bank',
      ayuda: 'Los estados de cuenta del mes de cada cuenta bancaria, en PDF (también escaneados).',
      acepta: '.pdf,.jpg,.jpeg,.png,application/pdf,image/*', tipos: [TipoDeDocumento.EstadoDeCuenta],
    },
    {
      clave: 'control', titulo: 'Control interno', icono: 'bi-journal-text',
      ayuda: 'Tickets, notas de venta o de compra, vales y comprobantes de transferencia: fotos o PDF.',
      acepta: '.pdf,.jpg,.jpeg,.png,.webp,application/pdf,image/*',
      tipos: [TipoDeDocumento.ControlInterno, TipoDeDocumento.Ticket, TipoDeDocumento.ComprobanteDePago],
    },
    {
      clave: 'nomina', titulo: 'Nómina', icono: 'bi-people',
      ayuda: 'Recibos de nómina timbrados (XML) y lo demás: lista de raya, incidencias, SUA, cédulas del IMSS.',
      acepta: '.xml,.pdf,.xlsx,.xls,.csv,.txt,.sua,.jpg,.jpeg,.png,application/pdf,image/*', tipos: [TipoDeDocumento.Nomina],
    },
  ];

  readonly clientes = signal<ClienteVisibleDto[] | null>(null);
  readonly clienteId = signal<string | null>(null);
  anio = new Date().getMonth() === 0 ? new Date().getFullYear() - 1 : new Date().getFullYear();
  // Lo normal es conciliar el mes que acaba de cerrar.
  mes = new Date().getMonth() === 0 ? 12 : new Date().getMonth();

  readonly resumen = signal<ResumenDelMesDto[]>([]);
  readonly avances = signal<Partial<Record<Zona, Avance>>>({});
  readonly error = signal<string | null>(null);

  // Antes de conciliar.
  readonly apertura = signal<SaldosDeAperturaDto | null>(null);
  catalogoOpcion: 'sat' | 'propio' = 'sat';
  readonly catalogo = signal<CuentaDelCatalogo[] | null>(null);
  readonly catalogoNombre = signal<string | null>(null);
  /** El catálogo propio que ya está en la bóveda del cliente: es el que usa la contabilidad electrónica. */
  readonly catalogoGuardado = signal<{ nombre: string; fecha: string } | null>(null);
  balanzaOpcion: 'existente' | 'subir' | 'ceros' | null = null;
  readonly balanzaArchivo = signal<{ nombre: string; texto: string; esXml: boolean; filas?: string[][] } | null>(null);
  /** Mes al que corresponde la balanza (sus saldos finales); el XML lo trae, en CSV se elige. */
  balanzaAnio = this.anio;
  balanzaMes = this.mes === 1 ? 12 : this.mes - 1;
  readonly revision = signal(0);
  readonly conciliando = signal(false);

  /** Cuántos documentos hay ya en el mes por zona. */
  readonly cargados = computed(() => {
    const r = this.resumen();
    const de = (z: ZonaDeCarga) => r.filter(x => x.anio === this.anio && x.mes === this.mes && z.tipos.includes(x.tipo)).reduce((t, x) => t + x.cantidad, 0);
    return Object.fromEntries(this.zonas.map(z => [z.clave, de(z)])) as Record<Zona, number>;
  });

  readonly subiendo = computed(() => Object.values(this.avances()).some(a => a && a.hechos < a.total));

  /** La balanza leída como saldos de apertura (o el error de lectura). */
  readonly aperturaLeida = computed<{ apertura: Apertura; anio: number; mes: number } | { error: string } | null>(() => {
    this.revision();
    const b = this.balanzaArchivo();
    if (!b) {
      return null;
    }
    try {
      const leida = b.esXml ? leerBalanzaXml(b.texto) : leerBalanzaTabla(b.filas ?? partirCsv(b.texto));
      if (!leida.renglones.length) {
        return { error: 'No encontré cuentas en el archivo de la balanza.' };
      }
      const anio = leida.anio ?? this.balanzaAnio;
      const mes = leida.mes ?? this.balanzaMes;
      const sig = mesSiguiente(anio, mes);
      return { apertura: aApertura(leida, this.catalogoOpcion === 'propio' ? this.catalogo() : null), anio: sig.anio, mes: sig.mes };
    } catch (e) {
      return { error: e instanceof Error ? e.message : 'No se pudo leer la balanza.' };
    }
  });

  /** Lo que falta contestar antes de conciliar (vacío = se puede). */
  readonly faltante = computed(() => {
    this.revision();
    const f: string[] = [];
    if (!this.clienteId()) {
      f.push('Elige el cliente.');
    }
    if (this.catalogoOpcion === 'propio' && !this.catalogo()?.length) {
      f.push('Sube el catálogo de cuentas (XML del SAT) o elige el del SAT.');
    }
    if (!this.balanzaOpcion) {
      f.push('Di con qué saldos se arranca: balanza de comprobación o saldos en ceros.');
    }
    if (this.balanzaOpcion === 'subir') {
      const a = this.aperturaLeida();
      if (!a) {
        f.push('Sube la balanza de comprobación.');
      } else if ('error' in a) {
        f.push(a.error);
      } else if (a.apertura.deudores !== a.apertura.acreedores) {
        f.push(`La balanza no cuadra: deudores ${a.apertura.deudores.toFixed(2)} y acreedores ${a.apertura.acreedores.toFixed(2)}.`);
      } else if (a.apertura.sinCodigo.length) {
        f.push(`Hay cuentas sin código agrupador: ${a.apertura.sinCodigo.slice(0, 5).join(', ')}${a.apertura.sinCodigo.length > 5 ? '…' : ''}. Sube el catálogo que las relaciona.`);
      }
    }
    if (this.subiendo()) {
      f.push('Espera a que terminen de subirse los archivos.');
    }
    return f;
  });

  ngOnInit(): void {
    const q = this.ruta.snapshot.queryParamMap;
    this.anio = Number(q.get('anio')) || this.anio;
    this.mes = Number(q.get('mes')) || this.mes;
    this.balanzaAnio = this.mes === 1 ? this.anio - 1 : this.anio;
    this.balanzaMes = this.mes === 1 ? 12 : this.mes - 1;
    this.perfiles.clientes().subscribe({
      next: lista => {
        this.clientes.set(lista);
        const elegido = lista.find(c => c.id === q.get('cliente')) ?? (lista.length === 1 ? lista[0] : null);
        this.elegir(elegido?.id ?? null);
      },
      error: e => this.error.set(mensajeDeError(e, 'No se pudo cargar la lista de clientes.')),
    });
  }

  elegir(id: string | null): void {
    this.clienteId.set(id);
    this.avances.set({});
    this.apertura.set(null);
    this.balanzaOpcion = null;
    this.balanzaArchivo.set(null);
    this.catalogo.set(null);
    this.catalogoNombre.set(null);
    this.catalogoGuardado.set(null);
    this.catalogoOpcion = 'sat';
    this.cambio();
    if (!id) {
      return;
    }
    void this.cargarCatalogoGuardado(id);
    this.mesa.apertura(id).subscribe({
      next: a => {
        this.apertura.set(a);
        this.balanzaOpcion = a ? 'existente' : null;
        this.cambio();
      },
      error: () => this.apertura.set(null),
    });
    this.recargarResumen();
  }

  cambiarPeriodo(): void {
    this.avances.set({});
    this.balanzaAnio = this.mes === 1 ? this.anio - 1 : this.anio;
    this.balanzaMes = this.mes === 1 ? 12 : this.mes - 1;
    this.cambio();
    this.recargarResumen();
  }

  /** Recalcula lo que depende de los campos con ngModel. */
  cambio(): void {
    this.revision.update(n => n + 1);
    const id = this.clienteId();
    if (id) {
      void this.router.navigate([], { queryParams: { cliente: id, anio: this.anio, mes: this.mes }, replaceUrl: true });
    }
  }

  private recargarResumen(): void {
    const id = this.clienteId();
    if (!id) {
      this.resumen.set([]);
      return;
    }
    this.boveda.resumen(id).subscribe({ next: r => this.resumen.set(r), error: () => this.resumen.set([]) });
  }

  /** A qué tipo de la bóveda (y mes) va cada archivo de una zona. */
  private destino(zona: Zona, a: File): [TipoDeDocumento, number?, number?] {
    switch (zona) {
      case 'facturas':
        return ES_XML(a) ? [TipoDeDocumento.Cfdi] : [TipoDeDocumento.Otro, this.anio, this.mes];
      case 'estados':
        return [TipoDeDocumento.EstadoDeCuenta, this.anio, this.mes];
      case 'control':
        return [TipoDeDocumento.ControlInterno, this.anio, this.mes];
      default:
        return ES_XML(a) ? [TipoDeDocumento.Cfdi] : [TipoDeDocumento.Nomina, this.anio, this.mes];
    }
  }

  /** Sube todo lo de una zona, cuatro a la vez; un error no detiene a los demás. Sin preguntas de relación. */
  async subir(evento: Event, zona: Zona): Promise<void> {
    const entrada = evento.target as HTMLInputElement;
    const archivos = Array.from(entrada.files ?? []);
    entrada.value = '';
    const id = this.clienteId();
    if (!id || !archivos.length) {
      return;
    }
    const avance: Avance = { total: archivos.length, hechos: 0, duplicados: 0, errores: [] };
    const poner = () => this.avances.update(a => ({ ...a, [zona]: { ...avance, errores: [...avance.errores] } }));
    poner();
    let siguiente = 0;
    const trabajador = async () => {
      while (siguiente < archivos.length) {
        const a = archivos[siguiente++];
        const [tipo, anio, mes] = this.destino(zona, a);
        try {
          const r = await firstValueFrom(this.boveda.subir(id, tipo, a, anio, mes));
          if (r.esDuplicado) {
            avance.duplicados++;
          }
        } catch (e) {
          avance.errores.push({ nombre: a.name, motivo: mensajeDeError(e, 'No se pudo subir.') });
        }
        avance.hechos++;
        poner();
      }
    };
    await Promise.all([trabajador(), trabajador(), trabajador(), trabajador()]);
    this.recargarResumen();
    this.cambio();
  }

  /** Si el cliente ya tiene catálogo propio en la bóveda, se lee y queda elegido: no hay que volver a subirlo. */
  private async cargarCatalogoGuardado(id: string): Promise<void> {
    try {
      const r = await firstValueFrom(this.boveda.buscar({ organizacionId: id, tipo: TipoDeDocumento.CatalogoDeCuentas, maxResultCount: 1 }));
      const doc = r.items[0];
      if (!doc || this.clienteId() !== id) {
        return;
      }
      const cuentas = leerCatalogo(await (await firstValueFrom(this.boveda.descargar(id, doc.id))).text());
      if (!cuentas.length || this.clienteId() !== id) {
        return;
      }
      this.catalogoGuardado.set({ nombre: doc.nombre, fecha: doc.subidoUtc });
      this.catalogo.set(cuentas);
      this.catalogoNombre.set(doc.nombre);
      this.catalogoOpcion = 'propio';
      this.cambio();
    } catch {
      // Sin catálogo guardado (o sin poder leerlo): se pregunta como siempre.
    }
  }

  async elegirCatalogo(evento: Event): Promise<void> {
    const entrada = evento.target as HTMLInputElement;
    let archivo = entrada.files?.[0];
    entrada.value = '';
    if (!archivo) {
      return;
    }
    this.error.set(null);
    const esXml = /\.xml$/i.test(archivo.name) || archivo.type.includes('xml');
    let cuentas: CuentaDelCatalogo[] = [];
    try {
      cuentas = esXml
        ? leerCatalogo(await archivo.text())
        : leerCatalogoTabla(ES_EXCEL(archivo.name) ? await filasDeExcel(await archivo.arrayBuffer()) : partirCsv(await archivo.text()));
    } catch {
      cuentas = [];
    }
    if (!cuentas.length) {
      this.catalogo.set(null);
      this.catalogoNombre.set(null);
      this.error.set(esXml
        ? 'Ese archivo no parece el catálogo de cuentas del SAT (XML con nodos «Ctas»).'
        : 'No encontré el catálogo en ese archivo: necesita columnas «cuenta» y «nombre» (y de preferencia «código agrupador»).');
    } else {
      if (!esXml) {
        // Del Excel o CSV se guarda su equivalente en XML del Anexo 24: la bóveda y el motor lo leen igual que el del SAT.
        const base = archivo.name.replace(/\.[^.]+$/, '');
        archivo = new File([catalogoAXml(cuentas, '', this.anio, this.mes)], `${base}.xml`, { type: 'application/xml' });
        if (!cuentas.some(x => x.codAgrup)) {
          this.error.set('El catálogo no trae código agrupador: las cuentas se relacionarán por nombre y conviene revisar las equivalencias en la mesa.');
        }
      }
      this.catalogo.set(cuentas);
      this.catalogoNombre.set(archivo.name);
      // Queda en la bóveda: el más reciente es el que usan la balanza, el auxiliar y la contabilidad electrónica.
      const id = this.clienteId();
      if (id) {
        this.boveda.subir(id, TipoDeDocumento.CatalogoDeCuentas, archivo).subscribe({
          next: r => this.catalogoGuardado.set({ nombre: r.documento.nombre, fecha: r.documento.subidoUtc }),
          error: e => this.error.set('El catálogo no se pudo guardar: ' + mensajeDeError(e)),
        });
      }
    }
    this.cambio();
  }

  async elegirBalanza(evento: Event): Promise<void> {
    const entrada = evento.target as HTMLInputElement;
    const archivo = entrada.files?.[0];
    entrada.value = '';
    if (!archivo) {
      return;
    }
    this.error.set(null);
    const esXml = /\.xml$/i.test(archivo.name) || archivo.type.includes('xml');
    if (ES_EXCEL(archivo.name)) {
      try {
        this.balanzaArchivo.set({ nombre: archivo.name, texto: '', esXml: false, filas: await filasDeExcel(await archivo.arrayBuffer()) });
      } catch {
        this.balanzaArchivo.set(null);
        this.error.set('No pude abrir ese Excel. Revisa que no tenga contraseña o guárdalo como CSV.');
      }
    } else {
      this.balanzaArchivo.set({ nombre: archivo.name, texto: await archivo.text(), esXml });
    }
    // El XML queda en la bóveda como respaldo de los saldos (un CSV no se guarda: la bóveda solo admite PDF, XML e imágenes).
    const id = this.clienteId();
    if (id && esXml) {
      this.boveda.subir(id, TipoDeDocumento.BalanzaDeComprobacion, archivo).subscribe({ error: () => undefined });
    }
    this.cambio();
  }

  /** Guarda los saldos con los que se arranca y pide la conciliación de todo; luego lleva a Cierre del mes. */
  async conciliar(): Promise<void> {
    const id = this.clienteId();
    if (!id || this.faltante().length || this.conciliando()) {
      return;
    }
    this.conciliando.set(true);
    this.error.set(null);
    try {
      if (this.balanzaOpcion === 'subir') {
        const a = this.aperturaLeida();
        if (a && !('error' in a)) {
          await firstValueFrom(this.mesa.guardarApertura(id, { anio: a.anio, mes: a.mes, saldos: a.apertura.saldos }));
        }
      } else if (this.balanzaOpcion === 'ceros') {
        await firstValueFrom(this.mesa.guardarApertura(id, { anio: this.anio, mes: this.mes, saldos: [] }));
      }
      await firstValueFrom(this.conciliacion.conciliarTodo(id, this.anio, this.mes));
      void this.router.navigate(['/conciliacion'], { queryParams: { cliente: id, anio: this.anio, mes: this.mes } });
    } catch (e) {
      this.error.set(mensajeDeError(e, 'No se pudo empezar la conciliación.'));
      this.conciliando.set(false);
    }
  }
}
