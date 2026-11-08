import Hero from "@/components/landing/Hero";
import MachineOnServer from "@/components/landing/MachineOnServer";
import FourWays from "@/components/landing/FourWays";
import RunRecord from "@/components/landing/RunRecord";
import Decisions from "@/components/landing/Decisions";
import BuiltWithTrax from "@/components/landing/BuiltWithTrax";
import ForYourTeam from "@/components/landing/ForYourTeam";
import LayerDiagram from "@/components/landing/LayerDiagram";
import DeploymentArchitectures from "@/components/landing/DeploymentArchitectures";
import WhereTraxStops from "@/components/landing/WhereTraxStops";
import PackageList from "@/components/landing/PackageList";

export default function Home() {
  return (
    <main>
      <Hero />
      <BuiltWithTrax />
      <ForYourTeam />
      <MachineOnServer />
      <RunRecord />
      <Decisions />
      <FourWays />
      <DeploymentArchitectures />
      <WhereTraxStops />
      <LayerDiagram />
      <PackageList />
    </main>
  );
}
