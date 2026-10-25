import { inject, Injectable } from '@angular/core';
import { RestService } from '@abp/ng.core';
import { BehaviorSubject, Observable, tap } from 'rxjs';

export interface ProfilePictureDto {
  hasPicture: boolean;
  /** La imagen ya lista para un <img src>: "data:image/jpeg;base64,...". */
  dataUrl?: string | null;
  sizeBytes: number;
}

@Injectable({ providedIn: 'root' })
export class ProfilePictureService {
  private readonly rest = inject(RestService);
  private readonly baseUrl = '/api/app/profile-picture';

  private readonly foto = new BehaviorSubject<string | null>(null);

  /**
   * La foto actual, o null si no hay.
   * <para>Vive en el servicio y no en la pantalla porque tiene DOS consumidores que no se
   * conocen entre sí: la página de foto de perfil y el avatar de la barra superior. Si
   * cada uno la pidiera por su cuenta, cambiar la foto actualizaría uno y dejaría el otro
   * con la anterior hasta recargar.</para>
   */
  readonly foto$ = this.foto.asObservable();

  get fotoActual(): string | null {
    return this.foto.value;
  }

  cargar(): Observable<ProfilePictureDto> {
    return this.obtener().pipe(tap(dto => this.foto.next(dto.dataUrl ?? null)));
  }

  /** Olvida la foto en memoria. Se llama al cerrar sesión: si no, el siguiente que entre
   * en este navegador vería el avatar del anterior hasta recargar la página. */
  limpiar(): void {
    this.foto.next(null);
  }

  private obtener(): Observable<ProfilePictureDto> {
    return this.rest.request<void, ProfilePictureDto>(
      { method: 'GET', url: this.baseUrl },
      { apiName: 'Default', skipHandleError: true },
    );
  }

  subir(imagen: Blob, nombre: string): Observable<ProfilePictureDto> {
    const formulario = new FormData();
    // 'file' TIENE que coincidir con el nombre del parámetro de UploadAsync: así es como
    // el model binder de ABP empareja el campo del formulario con el argumento.
    formulario.append('file', imagen, nombre);

    return this.rest
      .request<FormData, ProfilePictureDto>(
        { method: 'POST', url: this.baseUrl + '/upload', body: formulario },
        { apiName: 'Default', skipHandleError: true },
      )
      .pipe(tap(dto => this.foto.next(dto.dataUrl ?? null)));
  }

  borrar(): Observable<void> {
    return this.rest
      .request<void, void>(
        { method: 'DELETE', url: this.baseUrl },
        { apiName: 'Default', skipHandleError: true },
      )
      .pipe(tap(() => this.foto.next(null)));
  }
}
