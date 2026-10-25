import { describe, expect, it } from 'vitest';
import { SECCIONES, manualPara, nivelDeRoles } from '../ayuda/manual';
import { tokenDelFragmento } from './token';

describe('tokenDelFragmento', () => {
  it('lee el token del enlace del correo', () => {
    expect(tokenDelFragmento('#t=abcDEF123_-abcDEF123_-abcDEF123_-abcDEF12')).toBe('abcDEF123_-abcDEF123_-abcDEF123_-abcDEF12');
  });

  it('descarta lo que no parece un token', () => {
    expect(tokenDelFragmento('')).toBeNull();
    expect(tokenDelFragmento('#t=')).toBeNull();
    expect(tokenDelFragmento('#t=corto')).toBeNull();
    expect(tokenDelFragmento('#t=<script>alert(1)</script>aaaaaaaaaaaaaa')).toBeNull();
    expect(tokenDelFragmento('#otra=abcDEF123_-abcDEF123_-abcDEF123')).toBeNull();
  });
});

describe('manual por rol', () => {
  it('toma el rol más alto de la sesión', () => {
    expect(nivelDeRoles(['usuario'])).toBe(0);
    expect(nivelDeRoles(['usuario', 'Administrador'])).toBe(1);
    expect(nivelDeRoles(['owner'])).toBe(2);
    expect(nivelDeRoles(['admin'])).toBe(3);
    expect(nivelDeRoles([])).toBe(0);
    expect(nivelDeRoles(undefined)).toBe(0);
  });

  it('cada quien ve solo los temas de los permisos que tiene', () => {
    const titulos = (permisos: string[], nivel = 0) =>
      manualPara(p => permisos.includes(p), nivel).flatMap(s => s.temas.map(t => t.titulo));

    // Un cliente: ve lo de su organización; no gestiona clientes.
    const usuario = titulos([]);
    expect(usuario).toContain('Gestionar a la gente de tu organización');
    expect(usuario).toContain('Protege tu cuenta');
    expect(usuario).not.toContain('Dar acceso a un cliente');
    expect(usuario).not.toContain('Invitar a alguien');
    // El perfil fiscal: el cliente ve cómo llenar el suyo, no el de «cada cliente».
    expect(usuario).toContain('Llena tu perfil con tu constancia');
    expect(usuario).not.toContain('El perfil fiscal de cada cliente');

    const admin = titulos(['AuxiliarContable.Organizaciones', 'AbpIdentity.Users.Create', 'AbpIdentity.Users.Update']);
    expect(admin).toContain('Invitar a alguien');
    expect(admin).toContain('Dar acceso a un cliente');
    expect(admin).toContain('El perfil fiscal de cada cliente');
    expect(admin).not.toContain('Llena tu perfil con tu constancia');
    expect(admin).not.toContain('Gestionar a la gente de tu organización');
    expect(admin).not.toContain('Roles y permisos');

    // Lo del Developer no depende de un permiso sino del nivel.
    expect(titulos([], 2)).not.toContain('Invisible para los demás');
    expect(titulos([], 3)).toContain('Invisible para los demás');
  });

  it('las secciones sin temas desaparecen', () => {
    const secciones = manualPara(() => false, 0).map(s => s.titulo);
    expect(secciones).toEqual(['Lo básico', 'Perfil fiscal', 'Organizaciones']);
    expect(SECCIONES.length).toBeGreaterThan(1);
  });
});
