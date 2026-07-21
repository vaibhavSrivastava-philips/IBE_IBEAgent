import { Acknowledgement } from "./Acknowledgement";
import { CommunicationData } from "./CommunicationData";
import { HighFidelity } from "./HighFidelity";

export interface Workflow {
    id : number;
    name: string;
    inputID: number;
    outputIDs: Array<number>;
    highFidelity?: HighFidelity,
    acknowledgement?: Acknowledgement
    output?: CommunicationData;
    status?: string;
    input?: CommunicationData;
}
