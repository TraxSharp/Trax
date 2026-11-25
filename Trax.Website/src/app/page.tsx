import Hero from "@/components/landing/Hero";
import ForYourTeam from "@/components/landing/ForYourTeam";
import MachineOnServer from "@/components/landing/MachineOnServer";
import LiveUpdates from "@/components/landing/LiveUpdates";
import Decisions from "@/components/landing/Decisions";
import AgentsOnTheBoard from "@/components/landing/AgentsOnTheBoard";
import RunRecord from "@/components/landing/RunRecord";
import ShipIt from "@/components/landing/ShipIt";
import PackageList from "@/components/landing/PackageList";

// Each section that has a sample shows it in place of a diagram: Recovery's order, the features walk, ChatService, Recovery's
// refund and Board, in that order. Ship it follows the features walk: where the app runs, and how a release reaches it, is the next question.
export default function Home() {
  return (
    <main>
      <Hero />
      <MachineOnServer />
      <ForYourTeam />
      <ShipIt />
      <LiveUpdates />
      <Decisions />
      <AgentsOnTheBoard />
      <RunRecord />
      <PackageList />
    </main>
  );
}
