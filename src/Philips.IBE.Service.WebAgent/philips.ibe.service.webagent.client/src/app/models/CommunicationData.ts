import { CertificateConfigurations } from './CertificateConfigurations';
import { ProxyConfigurations } from './ProxyConfigurations ';
import { RetryConfigurations } from './RetryConfigurations';
import { TcpConnectionConfiguration } from './TcpConnectionConfiguration';
import { HttpConnectionConfiguration } from './HttpConnectionConfiguration';
import { S3Configuration } from './S3Configuration';
import { CacheConfiguration } from './CacheConfiguration';


export interface CommunicationData {
  id: number;
  isSSLEnabled: boolean;
  mode: string;
  name: string;
  type: string;
  tcpConfiguration: TcpConnectionConfiguration;
  httpConfiguration: HttpConnectionConfiguration;
  webSocketConfiguration: HttpConnectionConfiguration;
  certificateDetails: CertificateConfigurations;
  proxyConfigurations: ProxyConfigurations;
  connectionRetry: RetryConfigurations;
  messageRetry: RetryConfigurations;
  s3Configuration: S3Configuration;
  cacheConfiguration: CacheConfiguration;
}
