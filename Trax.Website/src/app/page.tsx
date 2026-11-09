import Hero from "@/components/landing/Hero";
import ForYourTeam from "@/components/landing/ForYourTeam";
import MachineOnServer from "@/components/landing/MachineOnServer";
import LiveUpdates from "@/components/landing/LiveUpdates";
import Decisions from "@/components/landing/Decisions";
import AgentsOnTheBoard from "@/components/landing/AgentsOnTheBoard";
import RunRecord from "@/components/landing/RunRecord";
import DeploymentArchitectures from "@/components/landing/DeploymentArchitectures";
import PackageList from "@/components/landing/PackageList";

// Each section that has a sample shows it in place of a diagram: Recovery's order, the features walk, ChatService, Recovery's
// refund and Board, in that order.
export default function Home() {
  return (
    <main>
      <Hero />
      <MachineOnServer />
      <ForYourTeam />
      <LiveUpdates />
      <Decisions />
      <AgentsOnTheBoard />
      <RunRecord />
      <DeploymentArchitectures />
      <PackageList />
    </main>
  );
}
