import { ServiceNode } from "./service-node";

export interface ServiceConfiguration {
    http:ServiceNode;
    tcp:ServiceNode;
    webSocketClient:ServiceNode;
    adt:ServiceNode;
}
