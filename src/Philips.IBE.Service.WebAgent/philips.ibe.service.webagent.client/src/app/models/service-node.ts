import { CertificateConfigurations } from "./CertificateConfigurations";
import { ProxyConfigurations } from "./ProxyConfigurations ";
import { RetryConfigurations } from "./RetryConfigurations";

export interface ServiceNode {
    endPoint?: string;
    ipAddress?: string;
    enableSSL: boolean;
    port?:number;
    isEnabled: boolean;
    contextPath?: string;
    sslConfiguration?:CertificateConfigurations;
    connectionRetry?: RetryConfigurations;
    proxyConfigurations?: ProxyConfigurations;
}
