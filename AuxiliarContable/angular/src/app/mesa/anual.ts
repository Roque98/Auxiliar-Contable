/** Vacío o no numérico -> null (no se capturó); lo demás, el número. */
export function aNumero(texto: string | number | null | undefined): number | null {
  if (texto === null || texto === undefined) {
    return null;
  }
  const limpio = String(texto).replace(/[$,\s]/g, '');
  if (limpio === '') {
    return null;
  }
  const n = Number(limpio);
  return Number.isFinite(n) ? n : null;
}
