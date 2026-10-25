import { Environment } from '@abp/ng.core';

const baseUrl = 'http://localhost:4201';

const oAuthConfig = {
  issuer: 'https://localhost:44338/',
  redirectUri: baseUrl,
  clientId: 'AuxiliarContable_App',
  responseType: 'code',
  scope: 'offline_access AuxiliarContable',
  requireHttps: true,
};

export const environment = {
  production: false,
  application: {
    baseUrl,
    name: 'AuxiliarContable',
  },
  oAuthConfig,
  apis: {
    default: {
      url: 'https://localhost:44338',
      rootNamespace: 'AuxiliarContable',
    },
    AbpAccountPublic: {
      url: oAuthConfig.issuer,
      rootNamespace: 'AbpAccountPublic',
    },
  },
} as Environment;
