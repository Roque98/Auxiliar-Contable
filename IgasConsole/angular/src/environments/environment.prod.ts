import { Environment } from '@abp/ng.core';

const baseUrl = 'https://console.ziacentro.mx';
const apiUrl = 'https://api.ziacentro.mx';

const oAuthConfig = {
  // El issuer debe coincidir EXACTAMENTE con el del documento de descubrimiento
  // que emite OpenIddict (App:SelfUrl en el servidor), barra final incluida.
  issuer: apiUrl + '/',
  redirectUri: baseUrl,
  clientId: 'IgasConsole_App',
  responseType: 'code',
  scope: 'offline_access IgasConsole',
  requireHttps: true,
};

export const environment = {
  production: true,
  application: {
    baseUrl,
    name: 'IgasConsole',
  },
  oAuthConfig,
  apis: {
    default: {
      url: apiUrl,
      rootNamespace: 'IgasConsole',
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
