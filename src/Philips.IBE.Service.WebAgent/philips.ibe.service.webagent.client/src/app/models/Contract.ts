import { Acknowledgement } from "./Acknowledgement";
import { CommunicationData } from "./CommunicationData";
import { HighFidelity } from "./HighFidelity";
import { Workflow } from "./Workflow";

export interface Contract {

  acknowledgement: Acknowledgement,
  highFidelity: HighFidelity,
  inputIDs:Array<number>,
  name: string,
  outputID: number,
  output?: CommunicationData;
  input?: CommunicationData[];
}

