// Tipos y lectura del JSON del motor, sin dependencias de Angular (se prueban con vitest).

/** Lo que Claude contestó (motor: servicio/normativa.py ESQUEMA). */
export interface AnalisisDelMotor {
  conclusion: string;
  tratamiento: { impuesto: string; resultado: string }[];
  fundamento: { capa: string; ordenamiento: string; articulo: string; dof: string; dice: string }[];
  chispazos: { pregunta: string; respuesta: string }[];
  excepciones_revisadas: { excepcion: string; resultado: string }[];
  condiciones: string[];
  datos_que_faltan: string[];
  pendientes: string[];
  confianza: 'alta' | 'media' | 'baja';
}

/** Lo que el índice de leyes armó (sin textos completos). */
export interface ExpedienteDelMotor {
  ejercicio: number;
  avisos: string[];
  perfil: { forma_juridica: string | null; sujetos: string[]; giros: { titulo: string; porcentaje?: number | null }[]; disparadores: Record<string, string[]> };
  nucleo: { clave: string; nombre: string; ultima_reforma: string | null }[];
  leyes_activadas: Record<string, string[]>;
  leyes_no_indexadas: string[];
  leyes_descartadas: string[];
  locales_por_revisar: string[];
}

export interface ResultadoNormativo {
  analisis: AnalisisDelMotor;
  expediente: ExpedienteDelMotor;
}

/** Lee el JSON guardado; si no se puede, null. */
export function leerResultado(json: string | null | undefined): ResultadoNormativo | null {
  if (!json) {
    return null;
  }
  try {
    const r = JSON.parse(json) as ResultadoNormativo;
    return r?.analisis ? r : null;
  } catch {
    return null;
  }
}
