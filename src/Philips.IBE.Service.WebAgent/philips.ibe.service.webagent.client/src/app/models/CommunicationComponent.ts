import { CommunicationData } from './CommunicationData';

export function getDefaultCommunicationData(): CommunicationData {
  return {
    id: 0,
    isSSLEnabled: false,
    mode: '',
    name: '',
    type: '',
    //maxThreads: 0,
    tcpConfiguration: {
      ipAddress: '',
      port: 0,
    },
    httpConfiguration: {
      endPoint: '',
    },
    webSocketConfiguration: {
      endPoint: '',
    },
    certificateDetails: {
      clientCertificatePath: '',
      clientCertificatePassword: '',
    },
    proxyConfigurations: {
      isEnabled: false,
      proxyAddress: '',
      proxyPort: '',
      proxyUsername: '',
      proxyPassword: '',
    },
    connectionRetry: {
      retryAttempts: 0,
      baseRetryDelayInSeconds: 0
    },
    messageRetry: {
      retryAttempts: 0,
      baseRetryDelayInSeconds: 0
    },
    s3Configuration: {
      serviceId: '',
      tenantName: '',
      collectorId: 'b5479cfd-3c91-428a-89b6-84dd57c06870',
      institutionName: '',
      gatewayUrl: '',
      iamHost: '',
      timeZone: '',
      privateKeyPath: '',
      privateKeyPassword: ''
    },
    cacheConfiguration: {
      cacheReconciliationEndPoint: '',
      cacheRelaodEndPoint: '',
      cacheCertificatePath: '',
      cacheCertificatePassword: ''
    }
  };
}
