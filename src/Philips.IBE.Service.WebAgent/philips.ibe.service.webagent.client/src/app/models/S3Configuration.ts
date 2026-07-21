export interface S3Configuration {
  serviceId: string;
  iamHost: string;
  gatewayUrl: string;
  tenantName: string;
  institutionName: string;
  timeout?: number;
  maxRetries?: number;
  collectorId: string;
  timeZone: string;
  privateKeyPath: string;
  privateKeyPassword: string;
}
