import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ConfigStateService, PermissionService } from '@abp/ng.core';
import { map } from 'rxjs';
import { MATRIZ, RESUMEN_POR_NIVEL, manualPara, nivelDeRoles } from './manual';
import { chatEncendido } from '../shared/chat';

/**
 * El manual, armado con lo que ESTA persona puede hacer: cada tema pide su permiso y solo
 * sale si lo tiene (ver manual.ts). Se recalcula si cambian los permisos de la sesión.
 */
@Component({
  selector: 'app-ayuda',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './ayuda.component.html',
  styleUrls: ['./ayuda.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AyudaComponent {
  private readonly permisos = inject(PermissionService);
  private readonly config = inject(ConfigStateService);
  private readonly usuario = toSignal(this.config.getOne$('currentUser'), { initialValue: null });
  // Cualquier cambio de permisos llega por aquí (UseDynamicClaims / recarga de configuración).
  private readonly concedidos = toSignal(this.config.getDeep$('auth.grantedPolicies').pipe(map(p => p ?? {})), {
    initialValue: {},
  });

  private readonly chat = chatEncendido();

  readonly matriz = MATRIZ;
  readonly nivel = computed(() => nivelDeRoles(this.usuario()?.roles));
  readonly resumen = computed(() => RESUMEN_POR_NIVEL[this.nivel()] ?? RESUMEN_POR_NIVEL[0]);

  readonly secciones = computed(() => {
    this.concedidos();
    return manualPara(p => this.permisos.getGrantedPolicy(p), this.nivel(), this.chat());
  });

  /** La tabla de roles solo le sirve a quien da de alta o reparte permisos. */
  readonly verMatriz = computed(() => {
    this.concedidos();
    return this.permisos.getGrantedPolicy('AbpIdentity.Users.Create') || this.permisos.getGrantedPolicy('AbpIdentity.Roles.ManagePermissions');
  });
}
