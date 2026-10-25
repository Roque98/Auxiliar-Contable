import { Environment } from '@abp/ng.core';

const baseUrl = 'https://contable.ziacentro.mx';
const apiUrl = 'https://api-contable.ziacentro.mx';

const oAuthConfig = {
  // El issuer debe coincidir EXACTAMENTE con el del documento de descubrimiento
  // que emite OpenIddict (App:SelfUrl en el servidor), barra final incluida.
  issuer: apiUrl + '/',
  redirectUri: baseUrl,
  clientId: 'AuxiliarContable_App',
  responseType: 'code',
  scope: 'offline_access AuxiliarContable',
  requireHttps: true,
};

export const environment = {
  production: true,
  application: {
    baseUrl,
    name: 'AuxiliarContable',
  },
  oAuthConfig,
  apis: {
    default: {
      url: apiUrl,
      rootNamespace: 'AuxiliarContable',
    },
    AbpAccountPublic: {
      url: oAuthConfig.issuer,
      rootNamespace: 'AbpAccountPublic',
    },
  },
  remoteEnv: {
    url: '/getEnvConfig',
    mergeStrategy: 'deepmerge'
  }
} as Environment;
