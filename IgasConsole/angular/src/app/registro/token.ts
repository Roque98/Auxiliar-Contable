/** Lee el token del fragmento del enlace del correo (`#t=…`); null si no parece uno. */
export function tokenDelFragmento(hash: string): string | null {
  const valor = new URLSearchParams(hash.replace(/^#/, '')).get('t');
  return valor && /^[A-Za-z0-9_-]{20,100}$/.test(valor) ? valor : null;
}
