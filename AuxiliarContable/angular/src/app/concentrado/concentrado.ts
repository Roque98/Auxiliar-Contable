/** Lógica de la hoja concentrado sin Angular (se prueba en concentrado.spec.ts). */

export const TEXTO_DEL_ESTADO: Record<string, string> = {
  por_pagar: 'Por pagar',
  vencido: 'Vencido',
  pagado: 'Pagado',
  a_favor: 'Saldo a favor',
  sin_impuesto: 'Sin impuesto',
  sin_calcular: 'Sin calcular',
  en_curso: 'En curso',
};

/** Etiqueta del design system para cada estado. */
export const ETIQUETA_DEL_ESTADO: Record<string, string> = {
  por_pagar: 'ig-etiqueta-alerta',
  vencido: 'ig-etiqueta-error',
  pagado: 'ig-etiqueta-exito',
  a_favor: 'ig-etiqueta-exito',
  sin_impuesto: 'ig-etiqueta-tinta',
  sin_calcular: 'ig-etiqueta-tinta',
  en_curso: 'ig-etiqueta-tinta',
};

/** Cálculos del ISR que son del mes (no acumulados de enero al mes). */
const ISR_MENSUAL = new Set(['resico_pf', 'arrendamiento', 'plataformas']);

/**
 * Qué detalle abre un renglón del cálculo y si es solo del mes. El IVA siempre es del mes; el ISR de personas
 * morales y de flujo es de enero al mes. «retenciones» depende de la obligación (lo que le retuvieron de ISR o de IVA).
 */
export function detalleDelRenglon(detalle: string | null | undefined, obligacion: string, clase: string | null | undefined):
  { detalle: string; soloDelMes: boolean } | null {
  if (!detalle) {
    return null;
  }
  if (detalle === 'retenciones') {
    return obligacion === 'iva' ? { detalle: 'retenciones_iva', soloDelMes: true } : { detalle: 'retenciones_isr', soloDelMes: ISR_MENSUAL.has(clase ?? '') };
  }
  if (detalle.startsWith('retenciones_a_terceros') || obligacion === 'iva' || detalle.startsWith('iva_')) {
    return { detalle, soloDelMes: true };
  }
  return { detalle, soloDelMes: ISR_MENSUAL.has(clase ?? '') };
}

/** Qué se le dice al cliente sobre un motivo según su clase: solo lo que es requisito tiene arreglo. */
export function consejoDelMotivo(clase: string, solucion: string): { titulo: string; texto: string } {
  switch (clase) {
    case 'requisito':
      return { titulo: 'Cómo hacer que sí cuente', texto: solucion || 'Pide al proveedor que corrija la factura.' };
    case 'indispensable':
      return {
        titulo: 'Por qué',
        texto: 'Que un gasto sea estrictamente indispensable depende de tu actividad, no de cómo se hizo la factura: aquí solo se explica.',
      };
    case 'inversion':
      return { titulo: 'Por qué', texto: 'Es una inversión: no se deduce de golpe, sino un porcentaje cada año (depreciación).' };
    case 'personal':
      return { titulo: 'Por qué', texto: 'Las deducciones personales se toman en tu declaración anual.' };
    case 'revisar':
      return { titulo: 'Falta un dato', texto: 'Hace falta saber algo más para decidir; tu contador lo revisa contigo.' };
    default:
      return { titulo: 'Por qué', texto: 'Así lo dice la ley para tu régimen: no depende de cómo se hizo la factura.' };
  }
}

export const TEXTO_DE_ESTADO_DE_FACTURA: Record<string, string> = {
  considerada: 'Considerada',
  no_considerada: 'No considerada',
  parcial: 'En parte',
  por_definir: 'Por definir',
  pago_parcial: 'Pagada en parte',
  cobrada: 'Cobrada',
  por_cobrar: 'Por cobrar',
  cobro_parcial: 'Cobrada en parte',
  facturada: 'Facturada',
  nota_de_credito: 'Nota de crédito',
  no_cuenta: 'No cuenta',
};

/** El mes que conviene ver al entrar: el anterior (el que se declara este mes). */
export function mesPorOmision(hoy: Date): { anio: number; mes: number } {
  const m = hoy.getMonth(); // 0-11: el mes anterior en 1-12
  return m === 0 ? { anio: hoy.getFullYear() - 1, mes: 12 } : { anio: hoy.getFullYear(), mes: m };
}

/** La sección del servidor para cada pestaña contable. */
export function seccionContable(pestana: string, subIngresos: string): string | null {
  switch (pestana) {
    case 'deudas_contables':
    case 'activos':
    case 'capital':
      return pestana;
    case 'ingresos':
      return subIngresos === 'contables' ? 'ingresos_contables' : null;
    default:
      return null;
  }
}

/** Agrupa los rubros por su grupo de la NIF B-6 («Pasivo a corto plazo»…), en el orden en que llegan. */
export function porGrupo<T extends { grupo: string; saldo: number }>(rubros: T[]): { grupo: string; total: number; rubros: T[] }[] {
  const grupos: { grupo: string; total: number; rubros: T[] }[] = [];
  for (const r of rubros) {
    let g = grupos.find(x => x.grupo === r.grupo);
    if (!g) {
      g = { grupo: r.grupo, total: 0, rubros: [] };
      grupos.push(g);
    }
    g.rubros.push(r);
    g.total = Math.round((g.total + r.saldo) * 100) / 100;
  }
  return grupos;
}
