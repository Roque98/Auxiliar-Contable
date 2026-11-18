import { RestService } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export enum TipoDeCredencial {
  Efirma = 0,
  Ciec = 1,
}

export enum AccionSobreCredencial {
  Guardar = 0,
  VerContrasena = 1,
  DescargarCer = 2,
  DescargarKey = 3,
  Borrar = 4,
  UsarEnDescargaMasiva = 5,
}

export interface CredencialesDto {
  organizacionId: string;
  efirma: {
    rfc: string | null;
    numeroDeCertificado: string | null;
    vigenteDesdeUtc: string;
    vigenteHastaUtc: string;
    diasParaVencer: number;
    porVencer: boolean;
    actualizadaUtc: string;
  } | null;
  ciec: { rfc: string; actualizadaUtc: string } | null;
  consentimientoUtc: string | null;
  puedeAdministrar: boolean;
  tieneNip: boolean;
}

/** El NIP de 4 dígitos o la contraseña de la cuenta: lo que demuestra que es la persona. */
export interface Verificacion {
  nip?: string;
  contrasenaDeCuenta?: string;
}

export interface UsoDeCredencialDto {
  cuando: string;
  quien: string;
  tipo: TipoDeCredencial;
  accion: AccionSobreCredencial;
}

/** `CredencialesAppService` (`/api/app/credenciales`): la e.firma y la CIEC del cliente, cifradas. */
@Injectable({ providedIn: 'root' })
export class CredencialesService {
  private readonly rest = inject(RestService);
  private readonly base = '/api/app/credenciales';
  private readonly opciones = { apiName: 'Default', skipHandleError: true };

  obtener(organizacionId: string): Observable<CredencialesDto> {
    return this.rest.request<void, CredencialesDto>({ method: 'GET', url: this.base, params: { organizacionId } }, this.opciones);
  }

  guardarEfirma(organizacionId: string, cerBase64: string, keyBase64: string, contrasena: string, consentimiento: boolean) {
    return this.rest.request<unknown, CredencialesDto>(
      { method: 'POST', url: `${this.base}/guardar-efirma/${organizacionId}`, body: { cerBase64, keyBase64, contrasena, consentimiento } },
      this.opciones,
    );
  }

  guardarCiec(organizacionId: string, rfc: string, contrasena: string, consentimiento: boolean) {
    return this.rest.request<unknown, CredencialesDto>(
      { method: 'POST', url: `${this.base}/guardar-ciec/${organizacionId}`, body: { rfc, contrasena, consentimiento } },
      this.opciones,
    );
  }

  borrar(organizacionId: string, tipo: TipoDeCredencial) {
    return this.rest.request<void, CredencialesDto>(
      { method: 'POST', url: `${this.base}/borrar/${organizacionId}`, params: { tipo } },
      this.opciones,
    );
  }

  revelar(organizacionId: string, tipo: TipoDeCredencial, v: Verificacion) {
    return this.rest.request<unknown, { contrasena: string }>(
      { method: 'POST', url: `${this.base}/revelar/${organizacionId}`, body: { tipo, ...v } },
      this.opciones,
    );
  }

  descargar(organizacionId: string, archivo: 'cer' | 'key', v: Verificacion) {
    return this.rest.request<unknown, { nombre: string; contenidoBase64: string }>(
      { method: 'POST', url: `${this.base}/descargar/${organizacionId}`, body: { archivo, ...v } },
      this.opciones,
    );
  }

  ponerNip(nip: string, contrasenaDeCuenta: string) {
    return this.rest.request<unknown, void>(
      { method: 'POST', url: `${this.base}/poner-nip`, body: { nip, contrasenaDeCuenta } },
      this.opciones,
    );
  }

  bitacora(organizacionId: string): Observable<UsoDeCredencialDto[]> {
    return this.rest.request<void, UsoDeCredencialDto[]>(
      { method: 'GET', url: `${this.base}/bitacora/${organizacionId}` },
      this.opciones,
    );
  }
}

export const NOMBRE_DE_ACCION: Record<AccionSobreCredencial, string> = {
  [AccionSobreCredencial.Guardar]: 'Guardó',
  [AccionSobreCredencial.VerContrasena]: 'Vio la contraseña',
  [AccionSobreCredencial.DescargarCer]: 'Descargó el .cer',
  [AccionSobreCredencial.DescargarKey]: 'Descargó el .key',
  [AccionSobreCredencial.Borrar]: 'Borró',
  [AccionSobreCredencial.UsarEnDescargaMasiva]: 'La usó para la Descarga Masiva del SAT',
};
